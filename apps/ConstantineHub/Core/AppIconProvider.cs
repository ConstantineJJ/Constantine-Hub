using System.Runtime.InteropServices;

namespace ConstantineHub.Core;

internal static class AppIconProvider
{
    private static readonly Lazy<Icon> AppIcon = new(LoadIcon);

    internal static Icon Current => AppIcon.Value;

    private static Icon LoadIcon()
    {
        var pngCandidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "WatermelonCat.png"),
            Path.Combine(AppContext.BaseDirectory, "Assets", "WatermelonCat.png")
        };

        foreach (var candidate in pngCandidates)
        {
            try
            {
                if (!File.Exists(candidate))
                    continue;

                using var bitmap = new Bitmap(candidate);
                var handle = bitmap.GetHicon();
                try
                {
                    using var borrowed = Icon.FromHandle(handle);
                    return (Icon)borrowed.Clone();
                }
                finally
                {
                    DestroyIcon(handle);
                }
            }
            catch
            {
                // Fall through to the packaged ICO or the system fallback.
            }
        }

        var icoCandidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "CH_Icon.ico"),
            Path.Combine(AppContext.BaseDirectory, "Assets", "CH_Icon.ico")
        };

        foreach (var candidate in icoCandidates)
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

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
