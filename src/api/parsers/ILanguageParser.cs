using GotoRef.Api.Domain.Models;

namespace GotoRef.Api.Parsers;

/// <summary>
/// Contrato comum para parsers de linguagem.
/// v1 → DotNet/ReflectionParser
/// v2 → TypeScript, v3 → Python, etc.
/// </summary>
public interface ILanguageParser
{
    string Language { get; }
    Task<List<NamespaceDetail>> ExtractTypesAsync(string packagePath, CancellationToken ct = default);
}
