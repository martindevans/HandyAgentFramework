using Microsoft.Extensions.AI;

namespace HandyAgentFramework;

/// <summary>
/// Provides a set of tools
/// </summary>
public interface IToolProvider
{
    IEnumerable<ToolDefinition> Tools { get; }
}

/// <summary>
/// Defines a tool for the tool with some metadata
/// </summary>
public record ToolDefinition
{
    /// <summary>
    /// Defines a tool for the tool with some metadata
    /// </summary>
    /// <param name="function">TThe actual tool function</param>
    /// <param name="isDefault">Is this a "default" tool that is always available</param>
    /// <param name="group">The logical group of this tool</param>
    public ToolDefinition(AIFunction function, bool isDefault, string? group = null)
    {
        Function = function;
        IsDefault = isDefault;
        Group = group ?? Guid.NewGuid().ToString();
    }

    /// <summary>The actual tool function</summary>
    public AIFunction Function { get; init; }

    /// <summary>Is this a "default" tool that is always available</summary>
    public bool IsDefault { get; init; }

    /// <summary>The logical group of this tool</summary>
    public string Group { get; init; }
}