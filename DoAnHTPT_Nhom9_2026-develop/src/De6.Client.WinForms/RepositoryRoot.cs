namespace De6.Client.WinForms;

internal static class RepositoryRoot
{
    /// <summary>Walks up from the executable/current directory to the folder holding global.json and the solution (V12: DataDirectory is repository-relative).</summary>
    public static string Find()
    {
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            DirectoryInfo? directory = new(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "global.json")) && Directory.Exists(Path.Combine(directory.FullName, "config")))
                    return directory.FullName;
                directory = directory.Parent;
            }
        }
        return Directory.GetCurrentDirectory();
    }
}
