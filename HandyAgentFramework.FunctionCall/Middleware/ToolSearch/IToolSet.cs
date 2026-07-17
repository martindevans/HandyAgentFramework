namespace HandyAgentFramework.FunctionCall.Middleware.ToolSearch;

/// <summary>
/// Provides a method to semantically search for tools that may answer a given query
/// </summary>
public interface IToolSet
{
    /// <summary>
    /// Perform a semantic search for tools that may match the given query
    /// </summary>
    /// <param name="query"></param>
    /// <param name="topK">Number of results to return</param>
    /// <param name="cancellation"></param>
    /// <returns></returns>
    Task<IReadOnlyList<SearchResult>> Search(string query, int? topK = default, CancellationToken cancellation = default);

    /// <summary>
    /// Try to get a tool with the given name
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    ToolDefinition? TryGetTool(string name);

    /// <summary>
    /// Get all of the default tools
    /// </summary>
    /// <returns></returns>
    IEnumerable<ToolDefinition> DefaultTools();

    /// <summary>
    /// Get all tools
    /// </summary>
    /// <returns></returns>
    IEnumerable<ToolDefinition> Tools();
    
    /// <summary>
    /// Get all of the tools that belong to the given group ID
    /// </summary>
    /// <param name="group"></param>
    /// <returns></returns>
    IEnumerable<ToolDefinition> GetToolGroup(string group);

    /// <summary>
    /// Search result from tool index
    /// </summary>
    /// <param name="Name">Name of the tool</param>
    /// <param name="Relevance">Relevance to the query (0 to 1)</param>
    public record SearchResult(string Name, float Relevance, ToolDefinition Tool);
}