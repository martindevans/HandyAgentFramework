using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.Collections;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace HandyAgentFramework.FunctionCall.Middleware.ToolSearch;

/// <summary>
/// Provides a tool allowing for the AI to search for new tools. Tools returned by the search are "activated" and will remain in context for
/// some turns before being cleaned away. Using the tool resets the counter. When a tool is activated all other tools in the same group are also available.
/// </summary>
public class ToolSearchProvider
    : AIContextProvider
{
    #region tool docstrings
    private const string ToolSearchDesc = "Request additional tools to be loaded based on a description of the functionality needed. Call " +
                                          "this when you need capabilities that are not available in your current tool set.";

    private const string ToolSearchQueryDesc = """
                                               A description of the capabilities requires. Should be a short description including the following fields:
                                               - Capability (e.g. "retrieve weather information")
                                               - Inputs (e.g. "A location such as a city of lat/long")
                                               - Outputs (e.g. "An up to date weather report")

                                               Rules:
                                               - Describe general functionality, not a specific task
                                               - Focus on what the tool can do, as if describing an API or function signature
                                               - Do NOT include proper nouns, dates, times, quantities, or user-specific details
                                               - Do NOT phrase the output as a question or instruction
                                               """;
    #endregion

    private readonly IToolSet _tools;
    private readonly Options _options;
    private readonly AIFunction _searchTool;

    public ToolSearchProvider(IToolSet tools, Options? options = null)
    {
        _tools = tools;
        _options = options ?? new Options();
        _searchTool = AIFunctionFactory.Create(ToolSearch);
    }

    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        // Get the tool search state
        var state = GetSessionState(context.Session);

        // Increment counters and clean up
        state.Increment(_options);

        // Output into list
        var tools = new List<AITool>();
        state.Output(tools, _tools, _searchTool);

        return new ValueTask<AIContext>(new AIContext()
        {
            Tools = tools
        });
    }

    #region middleware
    /// <summary>
    /// Intercepts tool calls and injects tool search results
    /// </summary>
    /// <param name="agent"></param>
    /// <param name="context"></param>
    /// <param name="next"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async ValueTask<object?> OnToolCallMiddleware(AIAgent agent, FunctionInvocationContext context, Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next, CancellationToken cancellationToken)
    {
        // Run the tool call that we're intercepting
        var result = await next(context, cancellationToken);

        // Get the session output tools list and add all previously activated tools to it
        var state = GetSessionState();
        var toolsOutputList = context.Options?.Tools;
        if (toolsOutputList != null)
            state.Output(toolsOutputList, _tools, _searchTool);

        // Activate whatever tool was just used, resetting it's counter
        state.Activate(context.CallContent.Name);

        // Return the original result of the call
        return result;
    }
    #endregion

    #region search tool
    [Description(ToolSearchDesc)]
    private async Task<ToolSearchResult> ToolSearch([Description(ToolSearchQueryDesc)] string query, CancellationToken cancellation = default)
    {
        // If there is no tools output list we cannot do tool search!
        var context = FunctionInvokingChatClient.CurrentContext;
        var toolsOutputList = context?.Options?.Tools;
        if (toolsOutputList == null)
            return new ToolSearchResult([]);

        // Search for tools that match the query
        var queryResults = await _tools.Search(query, topK:_options.TopK, cancellation:cancellation);
        if (queryResults.Count == 0)
            return new ToolSearchResult([]);

        // Apply cutoffs to select results
        var pCutoff = queryResults[0].Relevance * _options.TopP;
        var results = new ToolSearchResult(
            queryResults
               .Take(_options.TopK)
               .Where(a => a.Relevance >= pCutoff)
               .Select(a => a.Name)
               .ToList()
        );

        // Activate all these tools in the state
        var state = GetSessionState();
        foreach (var name in results.Tools)
            state.Activate(name);

        // Add these tools to the session tools list right now
        // ReSharper disable once AccessToModifiedClosure
        state.Output(toolsOutputList, _tools, _searchTool);

        return results;
    }

    [Description("A list of tools.")]
    private record ToolSearchResult(List<string> Tools);
    #endregion

    #region session state
    /// <summary>
    /// Get or create state. If the session is null this will create a blank new state every time
    /// </summary>
    /// <returns></returns>
    private static State GetSessionState(AgentSession? session = null)
    {
        session ??= AIAgent.CurrentRunContext?.Session;

        var state = session?.StateBag.GetValue<State>(State.StateKey);
        if (state == null)
        {
            state = new State();
            session?.StateBag.SetValue(State.StateKey, state);
        }

        return state;
    }

    private class State
    {
        public const string StateKey = $"{nameof(ToolSearchProvider)}.State";

        [JsonInclude] private List<ToolActivation> ActiveTools { get; set; } = [];

        /// <summary>
        /// Activate a tool and set it's counter to zero
        /// </summary>
        /// <param name="name"></param>
        public void Activate(string name)
        {
            // Reset the counter if the tool is already activated
            var idx = ActiveTools.FindIndex(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
                ActiveTools[idx].Counter = 0;
            else
                ActiveTools.Add(new ToolActivation(name, 0));
        }

        /// <summary>
        /// Increment all counters and remove items over the threshold
        /// </summary>
        /// <param name="options"></param>
        public void Increment(Options options)
        {
            // Increment counters
            foreach (var toolActivation in ActiveTools)
                toolActivation.Counter++;

            // Remove everything over the threshold
            for (var i = ActiveTools.Count - 1; i >= 0; i--)
                if (ActiveTools[i].Counter > options.ActivationTurns)
                    ActiveTools.RemoveAt(i);
        }

        /// <summary>
        /// Output tools into the output list
        /// </summary>
        /// <param name="output"></param>
        /// <param name="tools"></param>
        /// <param name="extras">Extra tools to add</param>
        public void Output(IList<AITool> output, IToolSet tools, params Span<AITool> extras)
        {
            var groups = new Dictionary<string, GroupedTools>();

            // Process all of the active tools into group aggregations
            foreach (var active in ActiveTools)
            {
                // Get the tool, skip if it doesn't exist
                var activeTool = tools.TryGetTool(active.Name);
                if (activeTool == null)
                    continue;

                // Get the group aggregation
                if (!groups.TryGetValue(activeTool.Group, out var group))
                {
                    group = new();
                    groups[activeTool.Group] = group;
                }

                // Add this tool to it
                group.Add(activeTool);
            }

            // Now take all of the active groups and add all tools in the group
            foreach (var (groupName, group) in groups)
            {
                foreach (var grouped in tools.GetToolGroup(groupName))
                    group.Add(grouped);
            }

            // Keep a set of tools that are already in the output
            var outputTools = new HashSet<string>();
            foreach (var item in output)
                outputTools.Add(item.Name);

            // Output all of the default tools
            foreach (var @default in tools.DefaultTools())
            {
                if (!outputTools.Contains(@default.Function.Name))
                {
                    output.Add(@default.Function);
                    outputTools.Add(@default.Function.Name);
                }
            }

            // Output all of the grouped tools
            foreach (var group in groups)
            {
                foreach (var tool in group.Value)
                {
                    if (!outputTools.Contains(tool.Function.Name))
                    {
                        output.Add(tool.Function);
                        outputTools.Add(tool.Function.Name);
                    }
                }
            }

            // Output the extras
            foreach (var extra in extras)
            {
                if (!outputTools.Contains(extra.Name))
                {
                    output.Add(extra);
                    outputTools.Add(extra.Name);
                }
            }
        }

        private class GroupedTools
            : IEnumerable<ToolDefinition>
        {
            private readonly HashSet<string> _names = [];
            private readonly List<ToolDefinition> _items = [];

            public void Add(ToolDefinition tool)
            {
                if (_names.Add(tool.Function.Name))
                    _items.Add(tool);
            }

            public IEnumerator<ToolDefinition> GetEnumerator()
            {
                return _items.GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }
    }

    private record ToolActivation
    {
        public ToolActivation(string name, int counter)
        {
            Name = name;
            Counter = counter;
        }

        public string Name { get; }
        public int Counter { get; set; }
    }
    #endregion

    /// <summary>
    /// Configuration options for tool search
    /// </summary>
    /// <param name="ActivationTurns">Once a tool is returned by search how long does it remain active</param>
    /// <param name="TopK">Max number of tools to select</param>
    /// <param name="TopP">Relevance threshold, expressed as a factor of best result</param>
    public record Options(
        int ActivationTurns = 3,
        int TopK = 8,
        float TopP = 0.69f
    );
}