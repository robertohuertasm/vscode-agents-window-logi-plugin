using System.Diagnostics;

namespace AgentsWindow;

// Opens product protocol links with macOS `open`, which hands them to VS Code.
public static class Opener
{
    public static void Open(string link)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("/usr/bin/open", [link]) { UseShellExecute = false });
        }
        catch (Exception e)
        {
            Log.Error($"could not open a VS Code link: {e.Message}");
        }
    }
}
