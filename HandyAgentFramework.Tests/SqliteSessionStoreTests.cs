using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.Text.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace HandyAgentFramework.Tests;

[TestClass]
public class SqliteSessionStoreTests
{
    [TestMethod]
    public async Task Roundtrip()
    {
        var agent = new TestAgent();
        using var database = new TestDatabaseProvider();
        var store = new SqliteSessionStore.SqliteSessionStore(database);

        var session = (Session)await agent.CreateSessionAsync();

        await store.Save("ctx", "key", agent, session);

        var session2 = (Session)await store.Load("ctx", "key", agent);
            
        Assert.AreEqual(session.ID, session2.ID);
    }

    [TestMethod]
    public async Task MissingContext()
    {
        var agent = new TestAgent();
        using var database = new TestDatabaseProvider();
        var store = new SqliteSessionStore.SqliteSessionStore(database);

        var session = (Session)await agent.CreateSessionAsync();

        await store.Save("ctx", "key", agent, session);

        var session2 = (Session)await store.Load("ctx1", "key", agent);

        Assert.AreNotEqual(session.ID, session2.ID);
    }

    [TestMethod]
    public async Task MissingKey()
    {
        var agent = new TestAgent();
        using var database = new TestDatabaseProvider();
        var store = new SqliteSessionStore.SqliteSessionStore(database);

        var session = (Session)await agent.CreateSessionAsync();

        await store.Save("ctx", "key", agent, session);

        var session2 = (Session)await store.Load("ctx", "key1", agent);

        Assert.AreNotEqual(session.ID, session2.ID);
    }

    private class TestAgent
        : AIAgent
    {
        protected override async ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = new CancellationToken())
        {
            var s = new Session { ID = Guid.NewGuid() };
            return s;
        }

        protected override async ValueTask<JsonElement> SerializeSessionCoreAsync(AgentSession session, JsonSerializerOptions? jsonSerializerOptions = null, CancellationToken cancellationToken = new CancellationToken())
        {
            var state = new Serialized(((Session)session).ID.ToString());
            var el = JsonSerializer.SerializeToElement(state, jsonSerializerOptions);
            return el;
        }

        protected override async ValueTask<AgentSession> DeserializeSessionCoreAsync(JsonElement serializedState, JsonSerializerOptions? jsonSerializerOptions = null, CancellationToken cancellationToken = new CancellationToken())
        {
            var id = serializedState.Deserialize<Serialized>(jsonSerializerOptions)!;
            return new Session { ID = Guid.Parse(id.Value) };
        }

        protected override Task<AgentResponse> RunCoreAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null, CancellationToken cancellationToken = new CancellationToken())
        {
            throw new NotImplementedException();
        }

        protected override IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null, CancellationToken cancellationToken = new CancellationToken())
        {
            throw new NotImplementedException();
        }
    }

    private class Session
        : AgentSession
    {
        public Guid ID { get; set; }
    }

    private record Serialized(string Value);
}