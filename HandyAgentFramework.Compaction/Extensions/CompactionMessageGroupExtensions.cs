using Microsoft.Agents.AI.Compaction;

#pragma warning disable MAAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

namespace HandyAgentFramework.Compaction.Extensions;

public static class CompactionMessageGroupExtensions
{
    /// <summary>
    /// Mark a compaction message group as excluded
    /// </summary>
    /// <param name="group"></param>
    /// <param name="reason"></param>
    /// <returns>A value indicating if the group was newly excluded</returns>
    public static bool Exclude(this CompactionMessageGroup group, string reason)
    {
        if (group.IsExcluded)
            return false;

        group.IsExcluded = true;
        group.ExcludeReason = reason;
        return true;
    }
}