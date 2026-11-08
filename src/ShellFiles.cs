using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Kroshka;

internal static class ShellFiles
{
    [DllImport("ole32.dll")] static extern int CoInitializeEx(IntPtr reserved, uint flags);
    [DllImport("ole32.dll")] static extern void CoUninitialize();
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHParseDisplayName(string name, IntPtr bind, out IntPtr pidl, uint flags, out uint attributes);
    [DllImport("shell32.dll")] static extern int SHOpenFolderAndSelectItems(IntPtr pidl, uint count, IntPtr items, uint flags);
    [DllImport("shell32.dll")] static extern int SHGetKnownFolderIDList(ref Guid folder, uint flags, IntPtr token, out IntPtr pidl);
    [DllImport("shell32.dll")] static extern void ILFree(IntPtr pidl);
    [DllImport("shell32.dll")] static extern void SHChangeNotify(uint change, uint flags, IntPtr item1, IntPtr item2);
    [StructLayout(LayoutKind.Sequential)] struct RecycleInfo { public uint Size; public long Bytes, Items; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHQueryRecycleBinW(string root, ref RecycleInfo info);
    internal static long RecycleCount(string root)
    {
        var info = new RecycleInfo { Size = (uint)Marshal.SizeOf<RecycleInfo>() };
        Marshal.ThrowExceptionForHR(SHQueryRecycleBinW(root, ref info)); return info.Items;
    }

    public static Task RunSta(Action action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => {
            int hr = CoInitializeEx(IntPtr.Zero, 2);
            try { Marshal.ThrowExceptionForHR(hr); action(); done.SetResult(); }
            catch (Exception ex) { done.SetException(ex); }
            finally { if (hr >= 0) CoUninitialize(); }
        }) { IsBackground = true, Name = "Kroshka shell" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return done.Task;
    }

    internal static string ExistingLocation(string path)
    {
        if (path.Length == 2 && path[1] == ':') path += "\\";
        string? full = Path.GetFullPath(path);
        while (full != null && !File.Exists(full) && !Directory.Exists(full)) full = Path.GetDirectoryName(full);
        return full ?? throw new DirectoryNotFoundException("Расположение больше недоступно.");
    }
    public static Task OpenLocation(string path) => RunSta(() =>
    {
        string existing = ExistingLocation(path);
        if (Directory.Exists(existing))
        {
            Process.Start(new ProcessStartInfo(existing) { UseShellExecute = true, Verb = "open" }); return;
        }
        IntPtr pidl = IntPtr.Zero;
        try
        {
            Marshal.ThrowExceptionForHR(SHParseDisplayName(existing, IntPtr.Zero, out pidl, 0, out _));
            Marshal.ThrowExceptionForHR(SHOpenFolderAndSelectItems(pidl, 0, IntPtr.Zero, 0));
        }
        finally { if (pidl != IntPtr.Zero) ILFree(pidl); }
    });

    // Notify the virtual Recycle Bin itself, not merely C:\$Recycle.Bin.
    // FLUSHNOWAIT queues delivery without waiting on Explorer on our UI thread.
    public static void NotifyRecycled(IReadOnlyList<string> removed)
    {
        if (removed.Count == 0) return;
        Guid recycle = new("B7534046-3ECB-4C18-BE4E-64CD4CB7D6AC");
        IntPtr pidl = IntPtr.Zero;
        try
        {
            Marshal.ThrowExceptionForHR(SHGetKnownFolderIDList(ref recycle, 0, IntPtr.Zero, out pidl));
            SHChangeNotify(0x00002000, 0x3000, pidl, IntPtr.Zero); // UPDATEDIR / IDLIST
            SHChangeNotify(0x00000800, 0x3000, pidl, IntPtr.Zero); // UPDATEITEM
        }
        finally { if (pidl != IntPtr.Zero) ILFree(pidl); }
        foreach (string folder in removed.Select(Path.GetDirectoryName).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            IntPtr text = Marshal.StringToCoTaskMemUni(folder);
            try { SHChangeNotify(0x00002000, 0x3005, text, IntPtr.Zero); } // PATHW
            finally { Marshal.FreeCoTaskMem(text); }
        }
    }
}
