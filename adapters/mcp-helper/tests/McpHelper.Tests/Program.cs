using McpHelper.Core;

await ReviewRegressions.Run();

var fixture = Path.Combine(Path.GetTempPath(), "mcp-helper-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixture);
var lib = Path.Combine(fixture, "Library");
var app = Path.Combine(fixture, "App");
Directory.CreateDirectory(lib);
Directory.CreateDirectory(app);
await File.WriteAllTextAsync(Path.Combine(lib, "Library.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><Target Name=\"NeverExecute\" BeforeTargets=\"Build\"><Error Text=\"UNTRUSTED BUILD EXECUTED\" /></Target></Project>");
await File.WriteAllTextAsync(Path.Combine(app, "App.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><ProjectReference Include=\"../Library/Library.csproj\" /></ItemGroup></Project>");
await File.WriteAllTextAsync(Path.Combine(lib, "Service.cs"), "namespace Demo; public partial class Service { public int Echo(int n) => n; public string Echo(string s) => s; }");
await File.WriteAllTextAsync(Path.Combine(lib, "Other.cs"), "namespace Demo; public partial class Service { public int Twice(int n) => Echo(n) + Echo(n); }");
await File.WriteAllTextAsync(Path.Combine(app, "Caller.cs"), "namespace Client; public class Caller { public int Run() => new Demo.Service().Echo(1); }");
var original = Directory.GetFiles(fixture, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllBytes);
var engine = new CodeEngine(fixture);
var found = await engine.QueryAsync("find_symbol", query: "Echo");
Check(found.OperationStatus == "PASS" && found.Results.Count == 2, "overloads are separate symbols");
var integer = found.Results.Single(s => s.Signature.Contains("int"));
var refs = await engine.QueryAsync("references", symbolId: integer.SymbolId);
Check(refs.Results.Count == 3, "semantic references cross partial files and project boundary");
var outline = await engine.QueryAsync("outline", file: "Library/Service.cs");
Check(outline.Results.Count == 3, "outline omits bodies and includes class/method declarations");
var preview = await engine.QueryAsync("preview", symbolId: integer.SymbolId, newName: "Repeat");
Check(preview.Edits.Count == 3 && preview.Edits.Sum(f => f.Changes.Count) == 4, "rename preview includes declaration and three exact references");
Check(preview.Edits.All(f => f.BaseSha256.Length == 64), "preview carries original byte hashes");
foreach (var pair in original) Check(pair.Value.SequenceEqual(File.ReadAllBytes(pair.Key)), "read and preview leave original bytes intact");
var outside = await engine.QueryAsync("outline", file: "../outside.cs");
Check(outside.ErrorCode == "E_SCOPE", "traversal denied");
var page = await engine.QueryAsync("find_symbol", query: "Echo", limit: 1);
Check(page.Results.Count == 1 && page.Truncated, "pagination reports truncation");
var invalid = await engine.QueryAsync("preview", symbolId: integer.SymbolId, newName: "not valid");
Check(invalid.ErrorCode == "E_ARGUMENT", "invalid rename rejected");
await File.AppendAllTextAsync(Path.Combine(lib, "Service.cs"), " // changed");
var stale = await engine.QueryAsync("references", symbolId: integer.SymbolId, expectedSnapshot: found.SnapshotId);
Check(stale.ErrorCode == "E_STALE", "expected snapshot rejects changed bytes");
Check(!Directory.Exists(Path.Combine(lib, "obj")), "analysis never invokes project restore or build");
var unicodePath = Path.Combine(lib, "Юникод.cs");
await File.WriteAllTextAsync(unicodePath, "namespace Demo; public class Юникод { string face = \"😀\"; public int Значение() => 1; }\r\n", new System.Text.UTF8Encoding(true));
var unicode = await engine.QueryAsync("find_symbol", query: "Значение");
Check(unicode.Results.Count == 1 && unicode.Results[0].File.EndsWith("Юникод.cs"), "Unicode paths and identifiers");
var unicodePreview = await engine.QueryAsync("preview", symbolId: unicode.Results[0].SymbolId, newName: "Счётчик", expectedSnapshot: unicode.SnapshotId);
var edit = unicodePreview.Edits.Single().Changes.Single();
Check(edit.Range.Start.Character == "namespace Demo; public class Юникод { string face = \"😀\"; public int ".Length, "UTF-16 positions count surrogate pairs correctly");
Check(unicodePreview.Edits[0].BaseSha256 == Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(unicodePath))), "BOM and CRLF included in raw source hash");
await File.WriteAllTextAsync(Path.Combine(lib, "Bad.cs"), "class Bad {}", System.Text.Encoding.Unicode);
Check((await engine.QueryAsync("find_symbol", query: "Bad")).ErrorCode == "E_ENCODING", "unsupported encodings fail explicitly");
File.Delete(Path.Combine(lib, "Bad.cs"));
await File.WriteAllTextAsync(Path.Combine(lib, "Unsafe.csproj"), "<!DOCTYPE Project [<!ENTITY x 'unsafe'>]><Project>&x;</Project>");
Check((await engine.QueryAsync("find_symbol", query: "Echo")).ErrorCode == "E_INPUT", "XML DTD is prohibited");
File.Delete(Path.Combine(lib, "Unsafe.csproj"));
await File.WriteAllTextAsync(Path.Combine(app, "App.csproj"), "<Project><ItemGroup><ProjectReference Include=\"../../outside.csproj\" /></ItemGroup></Project>");
Check((await engine.QueryAsync("find_symbol", query: "Echo")).ErrorCode == "E_SCOPE", "linked projects outside root rejected");
var link = Path.Combine(fixture, "escape");
Directory.CreateSymbolicLink(link, Path.GetTempPath());
Check((await engine.QueryAsync("find_symbol", query: "Echo")).ErrorCode == "E_SCOPE", "directory symlinks rejected");
Directory.Delete(link);
await File.WriteAllBytesAsync(Path.Combine(lib, "TooLarge.cs"), new byte[1024 * 1024 + 1]);
Check((await engine.QueryAsync("find_symbol", query: "Echo")).ErrorCode == "E_LIMIT", "oversized source rejected before semantic analysis");
Console.WriteLine("CORE PASS");

static void Check(bool ok, string message)
{
    if (!ok) throw new Exception("FAIL: " + message);
    Console.WriteLine("PASS: " + message);
}
