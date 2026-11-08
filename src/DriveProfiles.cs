namespace Kroshka;

internal static class DriveProfiles
{
    public static string DirectoryFor(string home, string primaryRoot, string drive)
    {
        string root = Path.GetPathRoot(Path.GetFullPath(drive))!;
        if (root.Length != 3 || root[1] != ':') throw new ArgumentException("Выбери локальный диск");
        if (string.Equals(root, Path.GetPathRoot(primaryRoot), StringComparison.OrdinalIgnoreCase)) return home;
        return Path.Combine(home, "drives", char.ToUpperInvariant(root[0]).ToString());
    }
}
