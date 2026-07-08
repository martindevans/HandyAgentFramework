using HandyAgentFramework.Compaction;
using JetBrains.Annotations;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;

namespace HandyAgentFramework.Tests;

#pragma warning disable MAAI001

[TestClass]
public class EphemeralMessageCompactionTests
{
    [UsedImplicitly] public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task NoChangeEmptyInput()
    {
        var input = new CompactionMessageIndex([ ]);
        var compact = new EphemeralMessageCompaction();

        Assert.IsFalse(await compact.CompactAsync(input, cancellationToken: TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task NoChangeSystemPrompt()
    {
        var input = new CompactionMessageIndex([]);
        input.InsertGroup(0, CompactionGroupKind.System, [
            new ChatMessage(ChatRole.System, "System prompt")
        ], 0);

        var compact = new EphemeralMessageCompaction();

        Assert.IsFalse(await compact.CompactAsync(input, cancellationToken: TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task NoChangeNoEphemeral()
    {
        var input = new CompactionMessageIndex([]);
        input.AddGroup(CompactionGroupKind.System, [ new ChatMessage(ChatRole.System, "System prompt") ], 0);
        input.AddGroup(CompactionGroupKind.User, [ new ChatMessage(ChatRole.User, "Hi") ], 1);
        input.AddGroup(CompactionGroupKind.AssistantText, [ new ChatMessage(ChatRole.Assistant, "Hello!") ], 2);

        var compact = new EphemeralMessageCompaction();

        Assert.IsFalse(await compact.CompactAsync(input, cancellationToken: TestContext.CancellationToken));
    }
}