namespace McpHelper.Core;

public sealed record Position(int Line, int Character);
public sealed record SourceRange(Position Start, Position End);
public sealed record CodeLocation(string SymbolId, string Name, string Kind, string Signature,
    string File, SourceRange Range, string Role = "declaration");
public sealed record ProposedChange(SourceRange Range, string Before, string After);
public sealed record ProposedFile(string File, string BaseSha256, List<ProposedChange> Changes);

public sealed record QueryResult
{
    public string OperationStatus { get; init; } = "PASS";
    public string AnalysisCoverage { get; init; } = "partial";
    public string? ErrorCode { get; init; }
    public string? Message { get; init; }
    public string SnapshotId { get; init; } = "";
    public string PositionEncoding { get; init; } = "utf-16; zero-based line/character";
    public List<string> Warnings { get; init; } = ["MSBuild, generators, analyzers, package restore and project scripts are never executed. Conditional compilation and external packages may be unresolved. Dynamic references and unsaved editor buffers are not covered."];
    public List<CodeLocation> Results { get; init; } = [];
    public List<ProposedFile> Edits { get; init; } = [];
    public List<string> AffectedFiles { get; init; } = [];
    public int Offset { get; init; }
    public int Total { get; init; }
    public bool Truncated { get; init; }
}

internal sealed class ScopeException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
