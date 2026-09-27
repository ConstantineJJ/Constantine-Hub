namespace ConstantineHub.Core;

internal static class AppIconProvider
{
    private static readonly Lazy<Icon> AppIcon = new(LoadIcon);

    internal static Icon Current => AppIcon.Value;

    private static Icon LoadIcon()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "CH_Icon.ico"),
            Path.Combine(AppContext.BaseDirectory, "Assets", "CH_Icon.ico")
        };

        foreach (var candidate in candidates)
        {
            try
            {
                if (File.Exists(candidate))
                    return new Icon(candidate);
            }
            catch
            {
                // A broken optional icon must never prevent Hub startup.
            }
        }

        return (Icon)SystemIcons.Application.Clone();
    }
}
