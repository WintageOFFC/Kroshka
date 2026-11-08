using System.Collections.Concurrent;
using System.IO.Enumeration;

namespace Kroshka;

internal readonly record struct ScanFile(string Path, long Size);
internal readonly record struct ScanEntry(string Path, long Size, FileAttributes Attributes);

// Two metadata readers, a bounded queue, one SQLite writer. No file contents are opened.
internal static class FastWalker
{
    public static IEnumerable<ScanFile> Walk(string root, Func<string, bool> excluded, Func<bool> stop, Func<bool> paused, Action skipped)
    {
        using var cancel = new CancellationTokenSource();
        using var output = new BlockingCollection<ScanFile>(8192);
        var directories = new ConcurrentQueue<string>(); directories.Enqueue(root);
        int remaining = 1, readers = 2;
        Exception? failure = null;
        void WaitIfPaused()
        { while (paused() && !stop()) { cancel.Token.ThrowIfCancellationRequested(); Thread.Sleep(40); } }
        void Read()
        {
            try
            {
                var options = new EnumerationOptions { RecurseSubdirectories = false, IgnoreInaccessible = false,
                    AttributesToSkip = 0, ReturnSpecialDirectories = false, BufferSize = 64 * 1024 };
                while (!stop() && !cancel.IsCancellationRequested)
                {
                    WaitIfPaused();
                    if (!directories.TryDequeue(out string? dir))
                    { if (Volatile.Read(ref remaining) == 0) break; Thread.Sleep(2); continue; }
                    try
                    {
                        if (excluded(dir)) continue;
                        var entries = new FileSystemEnumerable<ScanEntry>(dir,
                            (ref FileSystemEntry e) => new(e.ToFullPath(), e.IsDirectory ? 0 : e.Length, e.Attributes), options);
                        foreach (var entry in entries)
                        {
                            cancel.Token.ThrowIfCancellationRequested();
                            WaitIfPaused(); if (stop()) return;
                            if (excluded(entry.Path)) continue;
                            if ((entry.Attributes & FileAttributes.Directory) != 0)
                            {
                                if (Monitor.SkipDirectory(new DirectoryInfo(entry.Path), entry.Attributes)) { skipped(); continue; }
                                Interlocked.Increment(ref remaining); directories.Enqueue(entry.Path);
                            }
                            else output.Add(new(Monitor.Key(entry.Path), entry.Size), cancel.Token);
                        }
                    }
                    catch (UnauthorizedAccessException) { skipped(); }
                    catch (IOException) { skipped(); }
                    finally { Interlocked.Decrement(ref remaining); }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Interlocked.CompareExchange(ref failure, ex, null); cancel.Cancel(); }
            finally { if (Interlocked.Decrement(ref readers) == 0) output.CompleteAdding(); }
        }
        var threads = Enumerable.Range(0, 2).Select(i => new Thread(Read) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "Kroshka metadata " + i }).ToArray();
        foreach (var t in threads) t.Start();
        try
        {
            foreach (var file in output.GetConsumingEnumerable()) { if (stop()) break; yield return file; }
            if (failure != null) throw new IOException("Ошибка чтения списка файлов", failure);
        }
        finally
        {
            cancel.Cancel();
            foreach (var t in threads) t.Join();
        }
    }
}
