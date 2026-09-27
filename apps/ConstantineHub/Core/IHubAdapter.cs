namespace ConstantineHub.Core;

internal enum AdapterState
{
    NotConfigured,
    Stopped,
    Starting,
    Running,
    External,
    Degraded,
    Failed
}

internal sealed record AdapterStatus(
    string Id,
    string DisplayName,
    AdapterState State,
    string Summary,
    IReadOnlyDictionary<string, string>? Details = null);

internal interface IHubAdapter
{
    string Id { get; }
    string DisplayName { get; }

    Task<AdapterStatus> GetStatusAsync(CancellationToken cancellationToken = default);
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task RestartAsync(CancellationToken cancellationToken = default);
    Task<string> DoctorAsync(CancellationToken cancellationToken = default);
}
