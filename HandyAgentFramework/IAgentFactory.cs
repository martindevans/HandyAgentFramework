using Microsoft.Agents.AI;

namespace HandyAgentFramework;

public interface IAgentFactory
{
    public AIAgent Create();
}