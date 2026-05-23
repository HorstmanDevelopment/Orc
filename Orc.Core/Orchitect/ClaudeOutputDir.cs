namespace Orc.Core.Orchitect;

internal static class ClaudeOutputDir
{
    public static void Reset(string outDir)
    {
        if (Directory.Exists(outDir))
        {
            foreach (var file in Directory.EnumerateFiles(outDir))
            {
                try { File.Delete(file); } catch { }
            }
            foreach (var sub in Directory.EnumerateDirectories(outDir))
            {
                try { Directory.Delete(sub, recursive: true); } catch { }
            }
        }
        else
        {
            Directory.CreateDirectory(outDir);
        }
    }

    public static IReadOnlyList<string> ListFiles(string outDir)
    {
        if (!Directory.Exists(outDir)) return [];
        return Directory.GetFiles(outDir, "*", SearchOption.TopDirectoryOnly)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();
    }
}
