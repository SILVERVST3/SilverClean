namespace SilverClean.Core
{
    public class DirNode
    {
        public string Name;
        public string FullPath;
        public long Size;       // total, incluindo subpastas
        public long OwnFiles;   // só arquivos diretamente nesta pasta
        public int FileCount;   // total de arquivos (recursivo)
        public DirNode Parent;
        public List<DirNode> Children = new();
    }

    public class FileEntry
    {
        public string Path { get; init; }
        public long Size { get; init; }
        public DateTime Modified { get; init; }
        public string SizeText => Fmt.Size(Size);
        public string ModifiedText => Modified.ToString("dd/MM/yyyy HH:mm");
    }

    public class DiskScanResult
    {
        public DirNode Root;
        public List<FileEntry> LargestFiles;
    }

    public class DiskAnalyzer
    {
        private const int TopFiles = 200;

        private static readonly EnumerationOptions Opt = new()
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            ReturnSpecialDirectories = false
        };

        private readonly PriorityQueue<FileEntry, long> _top = new();
        private long _files;
        private volatile string _current = "";

        public long FilesScanned => Interlocked.Read(ref _files);
        public string CurrentFolder => _current;

        public DiskScanResult Scan(string path, CancellationToken ct)
        {
            var root = new DirNode
            {
                Name = path,
                FullPath = path
            };
            Walk(new DirectoryInfo(path), root, ct);

            var largest = new List<FileEntry>();
            while (_top.Count > 0) largest.Add(_top.Dequeue());
            largest.Reverse();

            return new DiskScanResult { Root = root, LargestFiles = largest };
        }

        private void Walk(DirectoryInfo di, DirNode node, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _current = di.FullName;

            IEnumerable<FileSystemInfo> entries;
            try { entries = di.EnumerateFileSystemInfos("*", Opt); }
            catch { return; }

            try
            {
                foreach (var e in entries)
                {
                    if (e is FileInfo f)
                    {
                        long len;
                        try { len = f.Length; } catch { continue; }
                        node.Size += len;
                        node.OwnFiles += len;
                        node.FileCount++;
                        Interlocked.Increment(ref _files);

                        if (_top.Count < TopFiles)
                            _top.Enqueue(new FileEntry { Path = f.FullName, Size = len, Modified = SafeTime(f) }, len);
                        else if (_top.TryPeek(out _, out long min) && len > min)
                            _top.EnqueueDequeue(new FileEntry { Path = f.FullName, Size = len, Modified = SafeTime(f) }, len);
                    }
                    else if (e is DirectoryInfo d)
                    {
                        var child = new DirNode { Name = d.Name, FullPath = d.FullName, Parent = node };
                        Walk(d, child, ct);
                        node.Size += child.Size;
                        node.FileCount += child.FileCount;
                        node.Children.Add(child);
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { /* pasta ficou inacessível no meio */ }

            node.Children.Sort((a, b) => b.Size.CompareTo(a.Size));
        }

        private static DateTime SafeTime(FileInfo f)
        {
            try { return f.LastWriteTime; } catch { return DateTime.MinValue; }
        }
    }
}
