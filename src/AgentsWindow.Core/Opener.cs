using System.Diagnostics;

namespace AgentsWindow;

// Hands product protocol links to the OS, which passes them to VS Code: `open` on macOS,
// the shell's URL handler on Windows.
public static class Opener
{
    public static void Open(string link)
    {
        try
        {
            var start = OperatingSystem.IsWindows()
                ? new ProcessStartInfo(link) { UseShellExecute = true }
                : new ProcessStartInfo("/usr/bin/open", [link]) { UseShellExecute = false };
            using var process = Process.Start(start);
        }
        catch (Exception e)
        {
            Log.Error($"could not open a VS Code link: {e.Message}");
        }
    }
}
