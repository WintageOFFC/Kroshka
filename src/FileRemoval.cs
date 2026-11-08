using Microsoft.VisualBasic.FileIO;

namespace Kroshka;

internal sealed record RecycleResult(List<string> Removed, List<string> Failures, string? ShellWarning);

internal static class FileRemoval
{
    internal static string Validate(string path, string root, string data)
    {
        string full = Path.GetFullPath(path);
        string prefix = Path.GetFullPath(root).TrimEnd('\\') + "\\";
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new IOException("Файл вне выбранного диска");
        foreach (string protectedPath in new[] { data, AppContext.BaseDirectory })
        {
            string guarded = Path.GetFullPath(protectedPath).TrimEnd('\\');
            if (full.Equals(guarded, StringComparison.OrdinalIgnoreCase) || full.StartsWith(guarded + "\\", StringComparison.OrdinalIgnoreCase))
                throw new IOException("Файл используется Крошкой");
        }
        var attributes = File.GetAttributes(full);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0) throw new IOException("Выбрана папка или ссылка, а не обычный файл");
        return full;
    }
    internal static bool ConfirmedMissing(string path)
    {
        try { File.GetAttributes(path); return false; }
        catch (FileNotFoundException) { return true; }
        catch (DirectoryNotFoundException) { return true; }
        // Access denied and transient I/O errors are not evidence of deletion.
    }
    public static bool Confirm(IWin32Window owner, List<string> paths)
    {
        using var dialog = new Form { Text = "Удалить выбранные файлы?", ClientSize = new Size(700, 380), BackColor = Theme.Bg, ForeColor = Theme.Ink, Font = Theme.Font(10), StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false };
        RetroDisplay.Window(dialog);
        var label = new Label { Text = $"В корзину: {paths.Count} файлов.\nМесто освободится после очистки корзины. Файлы OneDrive удалятся и из облака.", Location = new Point(18, 14), Size = new Size(664, 55) };
        var list = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Text = string.Join("\r\n", paths), Location = new Point(18, 76), Size = new Size(664, 235), BackColor = Theme.Panel, ForeColor = Theme.Ink };
        var cancel = new SoftButton { Text = "Отмена", DialogResult = DialogResult.Cancel, Location = new Point(390, 328), Width = 120 };
        var confirm = new SoftButton { Text = "В корзину", DialogResult = DialogResult.OK, Location = new Point(522, 328), Width = 160 };
        dialog.Controls.AddRange([label, list, cancel, confirm]); dialog.AcceptButton = cancel; dialog.CancelButton = cancel;
        return dialog.ShowDialog(owner) == DialogResult.OK;
    }
    public static Task<RecycleResult> Recycle(IReadOnlyList<string> paths, string root, string data, bool shellDialogs = true)
    {
        var done = new TaskCompletionSource<RecycleResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new Thread(() =>
        {
            var failures = new List<string>(); var removed = new List<string>();
            foreach (string path in paths)
            {
                try
                {
                    string full = Validate(path, root, data);
                    FileSystem.DeleteFile(full, shellDialogs ? UIOption.AllDialogs : UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
                    if (ConfirmedMissing(full)) removed.Add(full); else failures.Add(path + " — файл остался на месте");
                }
                catch (OperationCanceledException) { failures.Add("Отменено: " + path); break; }
                catch (Exception ex) { failures.Add(path + " — " + ex.Message); }
            }
            string? warning = null;
            try { ShellFiles.NotifyRecycled(removed); } catch (Exception ex) { warning = ex.Message; }
            done.SetResult(new(removed, failures, warning));
        }) { IsBackground = true, Name = "Kroshka recycle" };
        worker.SetApartmentState(ApartmentState.STA); worker.Start(); return done.Task;
    }
}
