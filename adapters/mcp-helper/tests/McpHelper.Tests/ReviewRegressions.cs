using McpHelper.Core;

internal static class ReviewRegressions
{
    internal static async Task Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "helper-regressions-" + Guid.NewGuid().ToString("N"));
        var failures = new List<string>();
        string Fixture(string name, Dictionary<string, string> files)
        {
            var path = Path.Combine(root, name);
            foreach (var (relative, source) in files)
            {
                var file = Path.Combine(path, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, source);
            }
            return path;
        }
        void Check(bool value, string name)
        {
            Console.WriteLine((value ? "PASS: " : "FAIL: ") + name);
            if (!value) failures.Add(name);
        }
        var collision = new CodeEngine(Fixture("collision", new() { ["Demo.cs"] = "public class Demo { public int First() => 1; public int Second() => 2; public int Run() => First(); }" }));
        var first = await collision.QueryAsync("find_symbol", query: "First");
        var conflict = await collision.QueryAsync("preview", symbolId: first.Results.Single().SymbolId, newName: "Second", expectedSnapshot: first.SnapshotId);
        Check(conflict.OperationStatus == "FAIL" && conflict.ErrorCode == "E_CONFLICT" && conflict.Edits.Count == 0, "R2 conflicting rename is explicit and supplies no unsafe edits");
        var partial = new CodeEngine(Fixture("partial", new() { ["First.cs"] = "public partial class Split {}", ["Second.cs"] = "public partial class Split {}", ["Caller.cs"] = "public class Caller { public Split Make() => new Split(); }" }));
        var split = await partial.QueryAsync("find_symbol", query: "Split");
        var impact = await partial.QueryAsync("impact", symbolId: split.Results.Single().SymbolId);
        Check(impact.AffectedFiles.SequenceEqual(new[] { "Caller.cs", "First.cs", "Second.cs" }), "R4 impact includes every partial declaration file");
        var enums = new CodeEngine(Fixture("enums", new() { ["State.cs"] = "public enum State { Ready, Done } public class User { public State Get() => State.Ready; }" }));
        var ready = await enums.QueryAsync("find_symbol", query: "Ready");
        Check(ready.Results.Count == 1, "R5 enum member is discoverable");
        if (ready.Results.Count == 1)
        {
            var references = await enums.QueryAsync("references", symbolId: ready.Results[0].SymbolId);
            var preview = await enums.QueryAsync("preview", symbolId: ready.Results[0].SymbolId, newName: "Available");
            Check(references.Results.Count == 1 && preview.OperationStatus == "PASS" && preview.Total == 2, "R5 enum member references and rename preview");
        }
        var nestedPath = Fixture("nested", new()
        {
            ["ThisIsAVeryLongRootProjectName.csproj"] = "<Project/>", ["Sub/A.csproj"] = "<Project/>",
            ["Sub/Child.cs"] = "public class Child { public void Work() {} }",
            ["ConsumerExternalProjectNameLong/B.csproj"] = "<Project><ItemGroup><ProjectReference Include=\"../Sub/A.csproj\"/></ItemGroup></Project>",
            ["ConsumerExternalProjectNameLong/Caller.cs"] = "public class Caller { public void Run() => new Child().Work(); }"
        });
        var nested = new CodeEngine(nestedPath);
        var work = await nested.QueryAsync("find_symbol", query: "Work");
        var refs = await nested.QueryAsync("references", symbolId: work.Results.Single().SymbolId);
        Check(work.Results.Single().SymbolId.StartsWith("Sub/A.csproj::") && refs.Results.Count == 1, "R3 long root filename cannot steal a nested project's source or references");
        var unresolved = new CodeEngine(Fixture("unresolved", new() { ["Demo.cs"] = "public class Demo { Missing dependency; public void Run() {} }" }));
        var run = await unresolved.QueryAsync("find_symbol", query: "Run");
        var safe = await unresolved.QueryAsync("preview", symbolId: run.Results.Single().SymbolId, newName: "Execute");
        Check(safe.OperationStatus == "PASS" && safe.Edits.Count == 1 && safe.Warnings.Any(w => w.Contains("compilation errors")), "R2 pre-existing unresolved dependencies do not block a safe rename");
        if (failures.Count > 0) throw new Exception("Review regressions: " + string.Join("; ", failures));
    }
}
