using System.Reflection;
using System.Runtime.Loader;

namespace ConstantineHub.SmokeTests;

// Read-only release discovery using the actual installed old assembly, without launching its updater.
internal static class UpdateDiscoveryTests
{
    internal static async Task Run(string previousAssembly, string expectedTag)
    {
        var context = new AssemblyLoadContext("previous-hub-update-discovery", isCollectible: true);
        try
        {
            var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(previousAssembly));
            var currentVersion = assembly.GetName().Version ?? throw new Exception("Previous version missing");
            var serviceType = assembly.GetType("ConstantineHub.Core.UpdateService", throwOnError: true)!;
            var service = Activator.CreateInstance(serviceType, nonPublic: true)!;
            var method = serviceType.GetMethod("CheckForStableUpdateAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var operation = (Task)method.Invoke(service, [new Version(currentVersion.ToString(3)), CancellationToken.None])!;
            await operation;
            var update = operation.GetType().GetProperty("Result")!.GetValue(operation)
                ?? throw new Exception("Previous Hub did not discover an update");
            var tag = update.GetType().GetProperty("Tag")!.GetValue(update)?.ToString();
            if (tag != expectedTag) throw new Exception($"Expected {expectedTag}, discovered {tag}");
            Console.WriteLine($"PASS: actual installed {currentVersion} update service discovers {tag} from GitHub; no staging or installation invoked");
        }
        finally { context.Unload(); }
    }
}
