using HandyAgentFramework.Compaction.Extensions;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace HandyAgentFramework.Compaction;

#pragma warning disable MAAI001

/// <summary>
/// Find ephemeral messages and removes them. To mark a message as ephemeral, add <see cref="IsEphemeralMarker"/> to the AdditionalProperties of the message.
/// </summary>
public sealed class EphemeralMessageCompaction
    : CompactionStrategy
{
    private readonly Options _options;

    /// <summary>
    /// Find ephemeral messages and removes them.
    /// </summary>
    /// <param name="options"></param>
    public EphemeralMessageCompaction(Options? options = null)
        : base(CompactionTriggers.Always)
    {
        _options = options ?? new();
    }

    /// <summary>
    /// Messages with this key in AdditionalProperties are "ephemeral" and will be removed by this compaction stage.
    /// </summary>
    public const string IsEphemeralMarker = "HandyAgentFramework_Ephemeral";

    /// <summary>
    /// Optional tag to put into AdditionalProperties. If it exists then a message will only be
    /// deleted if there is a later message with the same ID.
    /// </summary>
    public const string EphemeralGroupId = "HandyAgentFramework_Ephemeral.GroupId";

    private const string Reason = "Ephemeral marker detected";

    protected override ValueTask<bool> CompactCoreAsync(CompactionMessageIndex index, ILogger logger, CancellationToken cancellationToken)
    {
        var modified = false;

        // List of indices with their group tags
        var ephemeralGroupCandidates = new List<(int i, HashSet<string> groups)>();
        
        // Loop over all messages finding ephemeral tagged groups
        for (var i = 0; i < index.Groups.Count - _options.Keep; i++)
        {
            var group = index.Groups[i];

            // Skip groups we don't care about
            if (group.IsExcluded)
                continue;
            if (group.Kind is not CompactionGroupKind.ToolCall and not CompactionGroupKind.AssistantText)
                continue;

            // Is this group ephemeral?
            var ephemeral = group.Messages.All(IsEphemeral);
            if (!ephemeral)
                continue;

            // Check for ephemeral GroupId (collect from all messages in the group)
            var groups = group
                .Messages
                .Select(a => a.AdditionalProperties?.GetValueOrDefault(EphemeralGroupId))
                .Where(a => a != null)
                .OfType<string>()
                .ToHashSet();
            
            // No groups to consider, immediately remove
            if (groups.Count == 0)
            {
                modified |= group.Exclude(Reason);
                continue;
            }

            // Store this item with it's groups
            ephemeralGroupCandidates.Add((i, groups));
        }
        
        // Remove the tags of all later groups from each group
        for (var i = 0; i < ephemeralGroupCandidates.Count; i++)
        {
            var seti = ephemeralGroupCandidates[i].groups;
            for (var j = i + 1; j < ephemeralGroupCandidates.Count; j++)
                seti.ExceptWith(ephemeralGroupCandidates[j].groups);
        }
        
        // Now remove all items which have an empty set (indicating they were superceded)
        foreach (var candidate in ephemeralGroupCandidates)
        {
            if (candidate.groups.Count == 0)
            {
                var group = index.Groups[candidate.i];
                modified |= group.Exclude(Reason);
            }
        }

        return new ValueTask<bool>(modified);
    }

    private static bool IsEphemeral(ChatMessage msg)
    {
        if (msg.AdditionalProperties == null)
            return false;
        if (!msg.AdditionalProperties.TryGetValue(IsEphemeralMarker, out var marker))
            return false;

        return marker is true;
    }

    public sealed record Options
    {
        /// <summary>
        /// Number of groups to ignore for compaction, guaranteeing that they are preserved.
        /// </summary>
        public int Keep { get; init; } = 0;
    }
}

public static class EphemeralMessageExtensions
{
    /// <summary>
    /// Mark this chat message as ephemeral, optionally with a group. Ephemeral messages are deleted when the <see cref="EphemeralMessageCompaction"/>
    /// stage runs. Messages with a group ID will not be deleted until another ephemeral message has the same group ID later in the context.
    /// </summary>
    /// <param name="message"></param>
    /// <param name="group"></param>
    /// <returns></returns>
    public static ChatMessage Ephemeral(this ChatMessage message, string? group = null)
    {
        message.AdditionalProperties ??= new();
        message.AdditionalProperties[EphemeralMessageCompaction.IsEphemeralMarker] = true;

        if (group != null)
            message.AdditionalProperties[EphemeralMessageCompaction.EphemeralGroupId] = group;

        return message;
    }
}