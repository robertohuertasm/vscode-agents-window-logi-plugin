using System.Diagnostics;

namespace AgentsWindow;

public static class VsCodeApp
{
    public const string BundleId = "com.microsoft.VSCode";

    // macOS only; elsewhere the check is skipped (untested) and keys go to the frontmost app.
    public static bool IsFrontmost()
    {
        if (!OperatingSystem.IsMacOS()) return true;
        var front = Run("/usr/bin/lsappinfo", "front")?.Trim();
        if (string.IsNullOrEmpty(front)) return false;
        var info = Run("/usr/bin/lsappinfo", "info", "-only", "bundleid", front);
        return info?.Contains($"\"{BundleId}\"", StringComparison.Ordinal) == true;
    }

    // Brings VS Code's last focused window to the front, starting VS Code if needed.
    public static void Activate()
    {
        if (!OperatingSystem.IsMacOS()) return;
        Run("/usr/bin/open", "-b", BundleId);
    }

    private static string? Run(string file, params string[] args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(file, args) { RedirectStandardOutput = true, UseShellExecute = false });
            if (p is null) return null;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(2000);
            return output;
        }
        catch (Exception e)
        {
            Log.Warning($"could not run {file}: {e.Message}");
            return null;
        }
    }
}
