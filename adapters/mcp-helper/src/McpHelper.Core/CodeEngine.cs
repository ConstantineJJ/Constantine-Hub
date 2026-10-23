using System.Collections.Immutable;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;

namespace McpHelper.Core;

/// <summary>Disk-only Roslyn workspace. Never evaluates MSBuild or executes project tooling.</summary>
public sealed class CodeEngine
{
    private readonly SourceScope scope;
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly Lazy<ImmutableArray<MetadataReference>> RuntimeReferences = new(() =>
        ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "")
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToImmutableArray());
    public string WorkspaceRoot => scope.Root;
    public string BackendVersion => typeof(CSharpCompilation).Assembly.GetName().Version!.ToString();
    public CodeEngine(string root) => scope = new(root);

    public async Task<QueryResult> QueryAsync(string operation, string? query = null, string? file = null,
        string? symbolId = null, string? newName = null, int offset = 0, int limit = 50,
        string? expectedSnapshot = null, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var token = timeout.Token;
        var entered = false;
        try
        {
            if (offset is < 0 or > 100000 || limit is < 1 or > 200 || query?.Length > 256 || symbolId?.Length > 4096)
                throw new ScopeException("E_ARGUMENT", "Invalid query or pagination limit.");
            if (file is not null) scope.Resolve(file);
            if (operation is not ("find_symbol" or "outline" or "references" or "impact" or "preview"))
                throw new ScopeException("E_ARGUMENT", "Unsupported operation.");
            if (operation == "preview" && (newName?.Length > 128 || !SyntaxFacts.IsValidIdentifier(newName)))
                throw new ScopeException("E_ARGUMENT", "A valid C# identifier is required.");
            await gate.WaitAsync(token);
            entered = true;
            var files = scope.Capture(token);
            var snapshot = SourceScope.Snapshot(files);
            if (expectedSnapshot is not null && expectedSnapshot != snapshot)
                throw new ScopeException("E_STALE", "Workspace changed since the supplied snapshot.");
            using var workspace = new AdhocWorkspace();
            var solution = BuildSolution(workspace, files);
            var declarations = await Declarations(solution, token);
            var result = new QueryResult { SnapshotId = snapshot, Offset = offset };
            List<CodeLocation> locations;
            if (operation == "outline")
            {
                if (file is null) throw new ScopeException("E_ARGUMENT", "A relative file is required.");
                var full = scope.Resolve(file);
                if (!files.Any(f => f.FullPath.Equals(full, StringComparison.OrdinalIgnoreCase)))
                    throw new ScopeException("E_NOT_FOUND", "File is not in the supported source snapshot.");
                locations = declarations.Where(d => d.Document.FilePath!.Equals(full, StringComparison.OrdinalIgnoreCase)).Select(d => d.Location).ToList();
            }
            else if (operation == "find_symbol")
            {
                if (string.IsNullOrWhiteSpace(query)) throw new ScopeException("E_ARGUMENT", "A symbol query is required.");
                locations = declarations.Where(d => d.Symbol.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .DistinctBy(d => d.Location.SymbolId).Select(d => d.Location).ToList();
            }
            else
            {
                var declaration = declarations.FirstOrDefault(d => d.Location.SymbolId == symbolId)
                    ?? throw new ScopeException("E_NOT_FOUND", "Symbol ID was not found in this snapshot.");
                if (operation == "preview")
                {
                    var renamed = await Renamer.RenameSymbolAsync(solution, declaration.Symbol,
                        new SymbolRenameOptions(), newName!, token);
                    var edits = new List<ProposedFile>();
                    foreach (var project in renamed.GetChanges(solution).GetProjectChanges())
                    foreach (var id in project.GetChangedDocuments())
                    {
                        var before = solution.GetDocument(id)!;
                        var after = renamed.GetDocument(id)!;
                        var text = await before.GetTextAsync(token);
                        var changes = await after.GetTextChangesAsync(before, token);
                        var proposals = changes.Select(c => new ProposedChange(Range(text.Lines.GetLinePositionSpan(c.Span)),
                            text.ToString(c.Span), c.NewText ?? "")).ToList();
                        if (edits.Sum(e => e.Changes.Count) + proposals.Count > 2000)
                            throw new ScopeException("E_LIMIT", "Rename preview edit budget exceeded.");
                        var source = files.Single(f => f.FullPath.Equals(before.FilePath, StringComparison.OrdinalIgnoreCase));
                        edits.Add(new(source.RelativePath, source.Hash, proposals));
                    }
                    result = result with { Edits = edits.OrderBy(e => e.File, StringComparer.Ordinal).ToList(),
                        AffectedFiles = edits.Select(e => e.File).Order(StringComparer.Ordinal).ToList(), Total = edits.Sum(e => e.Changes.Count) };
                    locations = [];
                }
                else
                {
                    locations = [];
                    var references = await SymbolFinder.FindReferencesAsync(declaration.Symbol, solution, token);
                    foreach (var reference in references)
                    foreach (var location in reference.Locations)
                    {
                        if (location.IsImplicit || location.Document.FilePath is null) continue;
                        locations.Add(new(declaration.Location.SymbolId, declaration.Symbol.Name,
                            declaration.Symbol.Kind.ToString(), declaration.Location.Signature,
                            Relative(location.Document.FilePath), Range(location.Location.GetLineSpan().Span), "reference"));
                    }
                    locations = locations.DistinctBy(l => (l.File, l.Range.Start.Line, l.Range.Start.Character)).ToList();
                    result = result with { AffectedFiles = locations.Select(l => l.File).Append(declaration.Location.File)
                        .Distinct().Order(StringComparer.Ordinal).ToList() };
                }
            }
            if (operation != "preview")
            {
                var sorted = locations.OrderBy(l => l.File, StringComparer.Ordinal).ThenBy(l => l.Range.Start.Line)
                    .ThenBy(l => l.Range.Start.Character).ToList();
                result = result with { Results = sorted.Skip(offset).Take(limit).ToList(), Total = sorted.Count,
                    Truncated = offset + limit < sorted.Count };
            }
            var errors = 0;
            foreach (var project in solution.Projects)
            {
                var compilation = await project.GetCompilationAsync(token);
                errors += compilation!.GetDiagnostics(token).Count(d => d.Severity == DiagnosticSeverity.Error);
            }
            if (errors > 0) result.Warnings.Add($"Roslyn reported {errors} compilation errors in the safe workspace. Results may omit unresolved symbols.");
            if (SourceScope.Snapshot(scope.Capture(token)) != snapshot)
                throw new ScopeException("E_STALE", "Workspace changed during analysis; result discarded.");
            return result;
        }
        catch (ScopeException ex) { return new() { OperationStatus = "FAIL", ErrorCode = ex.Code, Message = ex.Message }; }
        catch (OperationCanceledException) { return new() { OperationStatus = "FAIL", ErrorCode = "E_TIMEOUT", Message = "Analysis cancelled or timed out." }; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException or ArgumentException)
        { return new() { OperationStatus = "FAIL", ErrorCode = "E_INPUT", Message = "Source files or project declarations could not be read safely." }; }
        finally { if (entered) gate.Release(); }
    }

    private Solution BuildSolution(AdhocWorkspace workspace, List<InputFile> files)
    {
        var solution = workspace.CurrentSolution;
        var projects = files.Where(f => f.RelativePath.EndsWith(".csproj", StringComparison.Ordinal)).ToList();
        var ids = projects.ToDictionary(p => p.FullPath, _ => ProjectId.CreateNewId(), StringComparer.OrdinalIgnoreCase);
        var fallback = ProjectId.CreateNewId();
        solution = AddProject(solution, fallback, "LooseFiles");
        foreach (var project in projects)
        {
            solution = AddProject(solution, ids[project.FullPath], project.RelativePath);
            using var reader = XmlReader.Create(new StringReader(project.Text), new XmlReaderSettings
                { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 });
            var xml = XDocument.Load(reader);
            foreach (var reference in xml.Descendants().Where(e => e.Name.LocalName == "ProjectReference"))
            {
                var include = reference.Attribute("Include")?.Value;
                if (string.IsNullOrWhiteSpace(include)) continue;
                var target = scope.Resolve(Path.GetRelativePath(scope.Root,
                    Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project.FullPath)!, include.Replace('\\', Path.DirectorySeparatorChar)))));
                if (!ids.TryGetValue(target, out var id)) throw new ScopeException("E_SCOPE", "A project reference is outside the captured project set.");
                solution = solution.AddProjectReference(ids[project.FullPath], new ProjectReference(id));
            }
            if (xml.Descendants().Any(e => e.Name.LocalName == "ImplicitUsings" && e.Value == "enable"))
                solution = solution.AddDocument(DocumentId.CreateNewId(ids[project.FullPath]), "HelperImplicitUsings.g.cs",
                    SourceText.From("global using System; global using System.Collections.Generic; global using System.IO; global using System.Linq; global using System.Net.Http; global using System.Threading; global using System.Threading.Tasks;"));
        }
        foreach (var file in files.Where(f => f.RelativePath.EndsWith(".cs", StringComparison.Ordinal)))
        {
            var owner = projects.Where(p => file.FullPath.StartsWith(Path.GetDirectoryName(p.FullPath)! + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(p => p.FullPath.Length).FirstOrDefault();
            var project = owner is null ? fallback : ids[owner.FullPath];
            solution = solution.AddDocument(DocumentId.CreateNewId(project), file.RelativePath,
                SourceText.From(file.Text, Encoding.UTF8), filePath: file.FullPath);
        }
        return solution;
    }

    private static Solution AddProject(Solution solution, ProjectId id, string name) => solution
        .AddProject(ProjectInfo.Create(id, VersionStamp.Create(), name, "Workspace_" + id.Id.ToString("N"), LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest), metadataReferences: RuntimeReferences.Value));

    private sealed record Declaration(ISymbol Symbol, Document Document, CodeLocation Location);
    private async Task<List<Declaration>> Declarations(Solution solution, CancellationToken token)
    {
        var found = new List<Declaration>();
        foreach (var document in solution.Projects.SelectMany(p => p.Documents))
        {
            if (document.FilePath is null) continue;
            var root = await document.GetSyntaxRootAsync(token);
            var model = await document.GetSemanticModelAsync(token);
            foreach (var node in root!.DescendantNodes().Where(n => n is BaseTypeDeclarationSyntax or BaseMethodDeclarationSyntax
                or PropertyDeclarationSyntax or VariableDeclaratorSyntax or DelegateDeclarationSyntax or EventDeclarationSyntax))
            {
                token.ThrowIfCancellationRequested();
                var symbol = model!.GetDeclaredSymbol(node, token);
                if (symbol is null || symbol.IsImplicitlyDeclared || symbol.Kind == SymbolKind.Local) continue;
                var location = symbol.Locations.FirstOrDefault(l => l.IsInSource && l.SourceTree == model!.SyntaxTree);
                if (location is null) continue;
                var relative = Relative(document.FilePath);
                var id = document.Project.Name + "::" + (DocumentationCommentId.CreateDeclarationId(symbol)
                    ?? symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                found.Add(new(symbol, document, new(id, symbol.Name, symbol.Kind.ToString(),
                    symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat), relative, Range(location.GetLineSpan().Span))));
                if (found.Count > 50000) throw new ScopeException("E_LIMIT", "Declaration limit exceeded.");
            }
        }
        return found;
    }
    private string Relative(string full) => Path.GetRelativePath(scope.Root, full).Replace('\\', '/');
    private static SourceRange Range(LinePositionSpan span) => new(new(span.Start.Line, span.Start.Character), new(span.End.Line, span.End.Character));
}
