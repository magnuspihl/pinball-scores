namespace PinballScores.Services
{
    public class FileService
    {
        private FileSystemWatcher watcher;
        public event FileSystemEventHandler? FileChanged;

        public FileService(string path, string? extension = null, bool includeSubdirectories = true)
        {
            watcher = new FileSystemWatcher(path);
            watcher.NotifyFilter = NotifyFilters.LastWrite;
            if (extension != null)
                watcher.Filter = "*." + extension;
            watcher.Changed += new FileSystemEventHandler(OnChanged);
            watcher.EnableRaisingEvents = true;
            watcher.IncludeSubdirectories = includeSubdirectories;
        }

        private void OnChanged(object sender, FileSystemEventArgs args)
        {
            FileChanged?.Invoke(sender, args);
        }

        public static bool ShouldProcessFile(string? path)
        {
            if (path == null)
                return false;
            return ShouldProcessFile(File.GetLastWriteTimeUtc(path));
        }
        public static bool ShouldProcessFile(FileInfo file)
        {
            return ShouldProcessFile(file.LastWriteTimeUtc);
        }
        private static bool ShouldProcessFile(DateTime lastWrite)
        {
            return lastWrite > ConfigService.LastRun;
        }
    }
}