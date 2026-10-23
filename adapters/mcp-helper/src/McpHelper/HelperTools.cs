using System.ComponentModel;
using System.Text.Json;
using McpHelper.Core;
using ModelContextProtocol.Server;

namespace McpHelper;

[McpServerToolType]
public sealed class HelperTools(CodeEngine engine, HelperConfig config)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private static string Encode(object result)
    {
        var text = JsonSerializer.Serialize(result, Json);
        return text.Length <= 262144 ? text : "{\"operation_status\":\"FAIL\",\"error_code\":\"E_LIMIT\",\"message\":\"Response budget exceeded; narrow the query.\"}";
    }

    [McpServerTool(Name = "helper_status", ReadOnly = true), Description("Read-only Helper identity, workspace and backend capabilities. No project code is executed.")]
    public string Status() => Encode(new { version = "0.1.1", instance_id = config.InstanceId,
        workspace_root = engine.WorkspaceRoot, backend = "Roslyn AdhocWorkspace", backend_version = engine.BackendVersion,
        mode = "disk-read-only", analysis_coverage = "partial", apply_available = false,
        capabilities = new[] { "outline", "symbols", "references", "impact", "rename_preview" } });

    [McpServerTool(Name = "helper_info", ReadOnly = true), Description("Describe the supported API, scope, budgets and limitations. Optional operation selects a focused description.")]
    public string Info(string? operation = null) => Encode(new { operation, api_version = 1,
        operations = new[] { "helper_status", "helper_info", "helper_outline", "helper_find_symbol", "helper_find_references", "helper_impact", "helper_refactor_preview" },
        workflow = "Find symbol, retain symbol_id and snapshot_id, then request references/impact/preview with expected_snapshot.",
        limits = "One configured workspace; 1000 source/project files; 1 MiB/file; 32 MiB total; 30s/query; 200 results/page; UTF-8 sources; positions UTF-16 zero-based.",
        limitations = "No MSBuild/restore/analyzers/generators execution, editor buffers or full dynamic graph. All analysis coverage is partial. No apply, shell or repository mutation." });

    [McpServerTool(Name = "helper_outline", ReadOnly = true), Description("C# declarations in one relative workspace file, without method bodies.")]
    public async Task<string> Outline(string file, int offset = 0, int limit = 50, string? expectedSnapshot = null, CancellationToken cancellationToken = default)
        => Encode(await engine.QueryAsync("outline", file: file, offset: offset, limit: limit, expectedSnapshot: expectedSnapshot, cancellationToken: cancellationToken));

    [McpServerTool(Name = "helper_find_symbol", ReadOnly = true), Description("Find C# declarations by name. Overloads have distinct symbol IDs. Results are paginated.")]
    public async Task<string> FindSymbol(string query, int offset = 0, int limit = 50, CancellationToken cancellationToken = default)
        => Encode(await engine.QueryAsync("find_symbol", query: query, offset: offset, limit: limit, cancellationToken: cancellationToken));

    [McpServerTool(Name = "helper_find_references", ReadOnly = true), Description("Find semantic references for a symbol ID. Supply expectedSnapshot to reject source changes.")]
    public async Task<string> References(string symbolId, string expectedSnapshot, int offset = 0, int limit = 50, CancellationToken cancellationToken = default)
        => Encode(await engine.QueryAsync("references", symbolId: symbolId, expectedSnapshot: expectedSnapshot, offset: offset, limit: limit, cancellationToken: cancellationToken));

    [McpServerTool(Name = "helper_impact", ReadOnly = true), Description("Bounded semantic reference impact with affected files and explicitly partial analysis coverage.")]
    public async Task<string> Impact(string symbolId, string expectedSnapshot, int offset = 0, int limit = 50, CancellationToken cancellationToken = default)
        => Encode(await engine.QueryAsync("impact", symbolId: symbolId, expectedSnapshot: expectedSnapshot, offset: offset, limit: limit, cancellationToken: cancellationToken));

    [McpServerTool(Name = "helper_refactor_preview", ReadOnly = true), Description("Preview a C# symbol rename with exact edits and base file hashes. Does not write files or offer apply.")]
    public async Task<string> Preview(string symbolId, string newName, string expectedSnapshot, CancellationToken cancellationToken = default)
        => Encode(await engine.QueryAsync("preview", symbolId: symbolId, newName: newName, expectedSnapshot: expectedSnapshot, cancellationToken: cancellationToken));
}
