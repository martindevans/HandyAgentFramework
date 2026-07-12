using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HandyAgentFramework.FunctionCall.Middleware;

/// <summary>
/// Store for <see cref="FunctionCallLog"/>
/// </summary>
public interface IFunctionCallLogStore
{
    /// <summary>
    /// Log a successful tool call
    /// </summary>
    /// <param name="name"></param>
    /// <param name="args"></param>
    /// <param name="result"></param>
    /// <returns></returns>
    ValueTask LogCall(string name, AIFunctionArguments args, object? result);

    /// <summary>
    /// Log a failed tool call
    /// </summary>
    /// <param name="name"></param>
    /// <param name="args"></param>
    /// <param name="exception"></param>
    /// <returns></returns>
    ValueTask LogException(string name, AIFunctionArguments args, Exception exception);

    /// <summary>
    /// Get successful calls, optionally filtered by function name.
    /// </summary>
    /// <param name="name">Optional function name to filter by.</param>
    /// <param name="after"></param>
    /// <param name="before"></param>
    /// <returns>Sequence of logged successful calls.</returns>
    ValueTask<IReadOnlyList<SuccessfulCall>> GetSuccessfulCalls(string? name = null, DateTime? after = null, DateTime? before = null);

    /// <summary>
    /// Get call exceptions, optionally filtered by function name.
    /// </summary>
    /// <param name="name">Optional function name to filter by.</param>
    /// <param name="after"></param>
    /// <param name="before"></param>
    /// <returns>Sequence of logged exceptions.</returns>
    ValueTask<IReadOnlyList<ExceptionalCall>> GetExceptionalCalls(string? name = null, DateTime? after = null, DateTime? before = null);

    /// <summary>
    /// Represents a logged successful function call.
    /// </summary>
    public record SuccessfulCall(long Id, string Name, string ArgumentsJson, string? ResultJson, DateTime LoggedAt);

    /// <summary>
    /// Represents a logged function call exception.
    /// </summary>
    public record ExceptionalCall(long Id, string Name, string ArgumentsJson, string Exception, DateTime LoggedAt);
}

/// <summary>
/// Logs function calls to a store, must be attached as middleware
/// </summary>
public class FunctionCallLog
{
    private readonly IFunctionCallLogStore _store;

    public FunctionCallLog(IFunctionCallLogStore store)
    {
        _store = store;
    }
    
    public async ValueTask<object?> Middleware(
        AIAgent agent,
        FunctionInvocationContext context,
        Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next,
        CancellationToken cancellationToken)
    {
        var name = context.Function.Name;
        var args = context.Arguments;
        
        try
        {
            var result = await next(context, cancellationToken);
            await _store.LogCall(name, args, result);
            return result;
        }
        catch (Exception ex)
        {
            await _store.LogException(name, args, ex);
            throw;
        }
    }
}