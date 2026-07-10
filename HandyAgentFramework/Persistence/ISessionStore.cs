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

/// <summary>
/// A wrapper around an <see cref="AgentSession"/> which saves it when disposed
/// </summary>
public interface ISessionScope
    : IAsyncDisposable
{
    public AgentSession Session { get; }
}

public static class ISessionStoreExtensions
{
    public static async Task<ISessionScope> GetSessionScope(this ISessionStore store, string context, string key, AIAgent agent, CancellationToken cancellation = default)
    {
        var session = await store.Load(context, key, agent, cancellation);
        return new SessionScope(context, key, session, agent, store);
    }
    
    private class SessionScope
        : ISessionScope
    {
        private readonly string _context;
        private readonly string _key;
        private readonly AgentSession _session;
        private readonly AIAgent _agent;
        private readonly ISessionStore _store;

        public bool IsDisposed { get; private set; }

        public AgentSession Session
        {
            get
            {
                CheckDisposed();
                return _session;
            }
        }

        public SessionScope(string context, string key, AgentSession session, AIAgent agent, ISessionStore store)
        {
            _context = context;
            _key = key;
            _session = session;
            _agent = agent;
            _store = store;
        }

        private void CheckDisposed()
        {
            if (IsDisposed)
                throw new ObjectDisposedException(message:"SessionContext has already been disposed", objectName:null);
        }
        
        public async ValueTask DisposeAsync()
        {
            CheckDisposed();
            await _store.Save(_context, _key, _agent, _session);
        }
    }
}