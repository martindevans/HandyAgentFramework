using Microsoft.Agents.AI;

namespace HandyAgentFramework.Persistence;

/// <summary>
/// Saves and loads agent sessions
/// </summary>
public interface ISessionStore
{
    /// <summary>
    /// Load an agent session for the given agent from the context with the key. If none is found; creates a new session.
    /// </summary>
    /// <param name="context"></param>
    /// <param name="key"></param>
    /// <param name="agent"></param>
    /// <param name="cancellation"></param>
    /// <returns></returns>
    Task<AgentSession> Load(string context, string key, AIAgent agent, CancellationToken cancellation = default);
    
    /// <summary>
    /// Save the agent session to the given context with the key.
    /// </summary>
    /// <param name="context"></param>
    /// <param name="key"></param>
    /// <param name="agent"></param>
    /// <param name="session"></param>
    /// <param name="cancellation"></param>
    /// <returns></returns>
    Task Save(string context, string key, AIAgent agent, AgentSession session, CancellationToken cancellation = default);
}