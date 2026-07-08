using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.Text.Json.Serialization;
using static HandyAgentFramework.FunctionCall.Middleware.FunctionCallStatisticsMonitor;

namespace HandyAgentFramework.FunctionCall.Middleware;

/// <summary>
/// Tracks statistics of function calls
/// </summary>
public class FunctionCallStatisticsMonitor
    : AIContextProvider
{
    private readonly ProviderSessionState<State> _sessionState = new(
        _ => new State(),
        nameof(FunctionCallStatisticsMonitor)
    );

    /// <inheritdoc />
    public override IReadOnlyList<string> StateKeys => field ??= [ _sessionState.StateKey ];

    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = new CancellationToken())
    {
        // Get state object, do this first so it's initialised even if nothing happens!
        _sessionState.GetOrInitializeState(context.Session);

        return base.ProvideAIContextAsync(context, cancellationToken);
    }

    /// <inheritdoc />
    protected override ValueTask InvokedCoreAsync(InvokedContext context, CancellationToken cancellationToken = default)
    {
        // Get state object, do this first so it's initialised even if nothing happens!
        var state = _sessionState.GetOrInitializeState(context.Session);

        if (context.ResponseMessages is null)
            return ValueTask.CompletedTask;

        // Get the current time
        var clock = context.Agent.GetService<TimeProvider>() ?? TimeProvider.System;
        var utcNow = clock.GetUtcNow().DateTime;

        // Bury all calls one turn deeper
        state.IncrementDepth();
        
        // Keep a cache of call IDs to function calls
        Dictionary<string, FunctionCallContent>? callCache = null;
        
        // Process all messages in the response, looking for calls and responses
        foreach (var message in context.ResponseMessages)
        {
            foreach (var content in message.Contents)
            {
                if (content is FunctionCallContent fcc)
                    state.GetStatistics(fcc.Name).Call(utcNow);

                if (content is FunctionResultContent fcr)
                {
                    var call = GetCall(fcr.CallId, context.RequestMessages.Concat(context.ResponseMessages), ref callCache);
                    if (call != null)
                        state.GetStatistics(call.Name).Return(fcr.Exception != null);
                }
            }
        }
        
        return base.InvokedCoreAsync(context, cancellationToken);
    }

    private static FunctionCallContent? GetCall(string id, IEnumerable<ChatMessage> messages, ref Dictionary<string, FunctionCallContent>? cache)
    {
        if (cache == null)
        {
            cache = new Dictionary<string, FunctionCallContent>();

            foreach (var message in messages)
                foreach (var content in message.Contents)
                    if (content is FunctionCallContent fcc)
                        cache[fcc.CallId] = fcc;
        }
        
        return cache.GetValueOrDefault(id);
    }
    
    /// <summary>
    /// Statistics on a function call tool
    /// </summary>
    public class FunctionCallStatistics
    {
        public string Name { get; }
        
        /// <summary>
        /// Total number of calls to this tools
        /// </summary>
        public int Calls { get; internal set; }
        
        /// <summary>
        /// How many calls resulted in an exception
        /// </summary>
        public int Exceptions { get; internal set; }
        
        /// <summary>
        /// How many calls did not result in an exception
        /// </summary>
        public int Success { get; internal set; }
        
        /// <summary>
        /// How many messages since this tool was last called
        /// </summary>
        public int Depth { get; internal set; }
        
        /// <summary>
        /// UTC time of the last call
        /// </summary>
        public DateTime LastCall { get; internal set; }

        /// <summary>
        /// Statistics on a function call tool
        /// </summary>
        /// <param name="name">Unique name of the function</param>
        public FunctionCallStatistics(string name)
        {
            Name = name;
        }

        public void Call(DateTime time)
        {
            Calls++;
            Depth = 0;
            LastCall = time;
        }

        public void Return(bool exception)
        {
            if (exception)
                Exceptions++;
            else
                Success++;
        }
    }

    internal class State
        : IStatistics
    {
        [JsonInclude]
        private Dictionary<string, FunctionCallStatistics> _stats { get; set; } = [ ];

        public FunctionCallStatistics GetStatistics(string name)
        {
            if (!_stats.TryGetValue(name, out var result))
            {
                result = new FunctionCallStatistics(name);
                _stats[name] = result;
            }

            return result;
        }

        public void IncrementDepth()
        {
            foreach (var item in _stats.Values)
                item.Depth++;
        }
    }
    
    public interface IStatistics
    {
        FunctionCallStatistics GetStatistics(string name);
    }
}

public static class FunctionCallStatisticsMonitorExtensions
{
    public static IStatistics? TryGetFunctionCallStatistics(this AgentSessionStateBag? bag)
    {
        return bag?.GetValue<State>(nameof(FunctionCallStatisticsMonitor));
    }
}