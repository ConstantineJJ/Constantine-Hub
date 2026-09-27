using ConstantineHub.Core;

namespace ConstantineHub.Adapters.LocalFiles;

internal sealed class LocalFilesAdapter : TunnelProfileAdapterBase
{
    internal LocalFilesAdapter() : base("constantine-files")
    {
    }

    public override string Id => "local-files";
    public override string DisplayName => "Local Files MCP";

    internal LocalFilesSummary SettingsSummary => LocalFilesSettingsStore.GetSummary();

    protected override Task<IReadOnlyDictionary<string, string>> GetAdditionalDetailsAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var summary = SettingsSummary;
        IReadOnlyDictionary<string, string> details = new Dictionary<string, string>
        {
            ["Config"] = LocalFilesSettingsStore.ConfigPath,
            ["Config valid"] = summary.ConfigValid ? "yes" : "no",
            ["Allowed roots"] = summary.RootCount.ToString(),
            ["Write enabled"] = summary.AnyWrite ? "yes" : "no",
            ["Delete enabled"] = summary.AnyDelete ? "yes" : "no"
        };
        return Task.FromResult(details);
    }
}
