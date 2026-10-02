namespace ConstantineHub.Core;

internal sealed record ServiceConnection(string Name, AdapterState Tunnel, bool? BridgeConnected = null,
    string? SetupIssue = null);

internal enum ConnectionTone { Neutral, Ready, Pending, Error }

internal sealed record ConnectionOverview(string Title, string Detail, string Description, ConnectionTone Tone)
{
    internal static ConnectionOverview Build(IReadOnlyList<ServiceConnection> services,
        IReadOnlyDictionary<string, string>? actionErrors = null)
    {
        var descriptions = services.Select(s => $"{s.Name}: tunnel {s.Tunnel.ToString().ToLowerInvariant()}" +
            (s.BridgeConnected is null ? "" : s.BridgeConnected.Value ? ", bridge connected" : ", bridge offline") +
            (s.SetupIssue is null ? "" : "; " + s.SetupIssue)).ToList();
        if (actionErrors is not null)
            descriptions.AddRange(actionErrors.Select(e => e.Key + ": " + e.Value));
        var description = string.Join(Environment.NewLine, descriptions);
        ConnectionOverview Result(string title, IEnumerable<string> names, ConnectionTone tone)
            => new(title, string.Join(" · ", names.Distinct()), description, tone);
        var failed = services.Where(s => s.Tunnel is AdapterState.Failed or AdapterState.Degraded).Select(s => s.Name)
            .Concat(actionErrors?.Keys ?? Enumerable.Empty<string>()).ToArray();
        if (failed.Length > 0) return Result("Service error", failed, ConnectionTone.Error);
        if (services.Count == 0) return new("Checking connections…", "Waiting for services", "", ConnectionTone.Neutral);
        var setup = services.Where(s => s.Tunnel == AdapterState.NotConfigured || s.SetupIssue is not null).ToArray();
        if (setup.Length > 0) return Result("Setup required", setup.Select(s => s.Name), ConnectionTone.Pending);
        var starting = services.Where(s => s.Tunnel == AdapterState.Starting).ToArray();
        if (starting.Length > 0) return Result("Tunnels starting…", starting.Select(s => s.Name), ConnectionTone.Pending);
        var stopped = services.Where(s => s.Tunnel == AdapterState.Stopped).ToArray();
        var offline = services.Where(s => s.BridgeConnected == false).ToArray();
        if (offline.Length > 0 && stopped.Length < services.Count)
            return Result("Bridges offline", offline.Select(s => s.Name), ConnectionTone.Pending);
        if (stopped.Length > 0) return Result("Tunnels stopped", stopped.Select(s => s.Name), ConnectionTone.Neutral);
        return new("Connections ready", $"{services.Count} services connected", description, ConnectionTone.Ready);
    }
}
