namespace HandyAgentFramework.Models;

/// <summary>
/// Base interface for models with a name
/// </summary>
public interface IModel
{
    public string Name { get; }
}

/// <summary>
/// Base interface for models that have a limited context size
/// </summary>
public interface IModelContext
    : IModel
{
    public uint ContextSize { get; }
}

/// <summary>
/// Base interface for models that generate text
/// </summary>
public interface ITextGenerationModel
    : IModelContext
{
    
}

public interface IChatModel : ITextGenerationModel;
public interface IEmbeddingModel : IModelContext;
public interface IRerankingModel : IModelContext;