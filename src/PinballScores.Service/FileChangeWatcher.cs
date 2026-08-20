using Microsoft.Extensions.Logging;

namespace PinballScores.Service;

/// <summary>
/// Watches the save files and requests a run when they change, debounced.
///
/// Games write their save data in bursts and a FileSystemWatcher reports every
/// intermediate write, so reacting immediately risks reading a half-written file
/// and submitting garbage. The quiet period collapses a burst into one run.
/// </summary>
public sealed class FileChangeWatcher : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly RunQueue _queue;
    private readonly TimeSpan _debounce;
    private readonly ILogger _log;
    private readonly Lock _gate = new();
    private Timer? _pending;
    private bool _disposed;

    public FileChangeWatcher(RunQueue queue, TimeSpan debounce, ILogger log)
    {
        _queue = queue;
        _debounce = debounce;
        _log = log;
    }

    public void WatchDirectory(string path, string filter)
    {
        if (!Directory.Exists(path)) return;
        Add(new FileSystemWatcher(path, filter) { IncludeSubdirectories = false });
    }

    public void WatchFile(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (directory is null || !Directory.Exists(directory)) return;
        Add(new FileSystemWatcher(directory, Path.GetFileName(path)));
    }

    private void Add(FileSystemWatcher watcher)
    {
        watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName;
        watcher.Changed += OnChanged;
        watcher.Created += OnChanged;
        watcher.Renamed += OnChanged;
        watcher.Error += (_, e) => _log.LogWarning(e.GetException(), "File watcher error");
        watcher.EnableRaisingEvents = true;
        _watchers.Add(watcher);
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        lock (_gate)
        {
            if (_disposed) return;
            // Restart the quiet period on every event: the run happens once the
            // machine has stopped writing, not on the first write of a burst.
            _pending?.Dispose();
            _pending = new Timer(_ =>
            {
                _log.LogInformation("Change detected in {Name}, queueing a run", e.Name);
                _queue.Request(RunTrigger.FileChanged);
            }, null, _debounce, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _pending?.Dispose();
            foreach (var watcher in _watchers)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            _watchers.Clear();
        }
    }
}
