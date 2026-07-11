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