using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HandyAgentFramework;

public interface IAgentFactory
{
    public AIAgent Create();
}