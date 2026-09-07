using System.Text.Json;
using Kernel.Core;

namespace Strogo.Modules;

public class ModuleException(string stage, string code, string message, string? entityId = null, object? details = null, Exception? innerException = null) : Exception(message, innerException)
{
    public string Stage { get; } = stage;
    public string Code { get; } = code;
    public string? EntityId { get; } = entityId;
    public string? DetailsJson { get; } = JsonSerializer.Serialize(details ?? new { }, CanonicalJson.SerializerOptions);
}

public static class ModulesExceptionFactory
{
    public static ModuleException Error(string stage, string code, string? entityId = null, object? details = null)
        => new(stage, code, $"{stage}:{code}", entityId, details);

    public static ModuleException Error(string stage, string code, object? details)
        => new(stage, code, $"{stage}:{code}", null, details);
}
