using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using SixLabors.ImageSharp;

namespace CloudFileManager
{
    // ==========================================
    // 1. Core Component & Observer (組合與觀察者模式)
    // ==========================================
    
    public interface INodeObserver
    {
        void OnNodeVisited(FileSystemNode node, int currentCount, int totalCount);
    }

    public abstract class FileSystemNode
    {
        public string Name { get; set; }
        public string StorageName { get; set; }
        public string XmlName { get; set; }
        public string StoragePath => Parent == null ? StorageName : $"{Parent.StoragePath}/{StorageName}";
        public DateTime CreatedTime { get; set; } = DateTime.Now;
        public HashSet<string> Tags { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public DirectoryNode? Parent { get; set; }

        protected FileSystemNode(string name, string? storageName = null, string? xmlName = null)
        {
            Name = name;
            StorageName = storageName ?? name;
            XmlName = xmlName ?? name;
        }

        public abstract double GetSize();
        public abstract string GetExtension();
        public abstract void Accept(IVisitor visitor, List<INodeObserver> observers, ref int currentCount, int totalCount);
        public abstract FileSystemNode Clone(); // 用於原型模式以實現 Command 的複製貼上
    }

    public class DirectoryNode : FileSystemNode
    {
        private readonly List<FileSystemNode> _children = new();
        public IReadOnlyList<FileSystemNode> Children => _children;

        public DirectoryNode(string name, string? storageName = null, string? xmlName = null) : base(name, storageName, xmlName) { }

        public void Add(FileSystemNode component)
        {
            component.Parent = this;
            _children.Add(component);
        }

        public void Remove(FileSystemNode component)
        {
            _children.Remove(component);
            component.Parent = null;
        }

        public override double GetSize()
        {
            return _children.Sum(c => c.GetSize());
        }

        public override string GetExtension() => string.Empty;

        public override void Accept(IVisitor visitor, List<INodeObserver> observers, ref int currentCount, int totalCount)
        {
            currentCount++;
            foreach (var observer in observers) observer.OnNodeVisited(this, currentCount, totalCount);
            
            visitor.VisitDirectory(this);
            // 複製一份子節點清單以防在遍歷過程中發生結構異動（例如刪除指令）
            var tempChildren = _children.ToList();
            foreach (var child in tempChildren)
            {
                child.Accept(visitor, observers, ref currentCount, totalCount);
            }
            visitor.LeaveDirectory(this);
        }

        public override FileSystemNode Clone()
        {
            var clone = new DirectoryNode(Name, StorageName, XmlName);
            foreach (var tag in Tags) clone.Tags.Add(tag);
            foreach (var child in _children)
            {
                clone.Add(child.Clone());
            }
            return clone;
        }

        // Bonus: 排序功能
        public void SortChildren(string sortBy, bool ascending)
        {
            Func<FileSystemNode, object> keySelector = sortBy.ToLower() switch
            {
                "大小" => n => n.GetSize(),
                "副檔名" => n => n.GetExtension(),
                _ => n => n.Name
            };

            var sorted = ascending ? _children.OrderBy(keySelector).ToList() : _children.OrderByDescending(keySelector).ToList();
            _children.Clear();
            _children.AddRange(sorted);
        }
    }

    public abstract class FileNode : FileSystemNode
    {
        public double SizeKB { get; set; }

        protected FileNode(string name, double sizeKB, string? storageName = null, string? xmlName = null) : base(name, storageName, xmlName)
        {
            SizeKB = sizeKB;
        }

        public override double GetSize() => SizeKB;
        public override string GetExtension() => Path.GetExtension(Name);

        public override void Accept(IVisitor visitor, List<INodeObserver> observers, ref int currentCount, int totalCount)
        {
            currentCount++;
            foreach (var observer in observers) observer.OnNodeVisited(this, currentCount, totalCount);
            visitor.VisitFile(this);
        }
    }

    // 衍生特定檔案類型
    public class WordFile : FileNode
    {
        public int Pages { get; set; }
        public WordFile(string name, double sizeKB, int pages, string? storageName = null, string? xmlName = null) : base(name, sizeKB, storageName, xmlName) => Pages = pages;
        public override FileSystemNode Clone()
        {
            var clone = new WordFile(Name, SizeKB, Pages, StorageName, XmlName);
            foreach (var tag in Tags) clone.Tags.Add(tag);
            return clone;
        }
    }

    public class ImageFile : FileNode
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public ImageFile(string name, double sizeKB, int width, int height, string? storageName = null, string? xmlName = null) : base(name, sizeKB, storageName, xmlName) { Width = width; Height = height; }
        public override FileSystemNode Clone()
        {
            var clone = new ImageFile(Name, SizeKB, Width, Height, StorageName, XmlName);
            foreach (var tag in Tags) clone.Tags.Add(tag);
            return clone;
        }
    }

    public class TextFile : FileNode
    {
        public string Encoding { get; set; }
        public TextFile(string name, double sizeKB, string encoding, string? storageName = null, string? xmlName = null) : base(name, sizeKB, storageName, xmlName) => Encoding = encoding;
        public override FileSystemNode Clone()
        {
            var clone = new TextFile(Name, SizeKB, Encoding, StorageName, XmlName);
            foreach (var tag in Tags) clone.Tags.Add(tag);
            return clone;
        }
    }

    // ==========================================
    // 2. Visitor Pattern (訪問者模式：解耦業務邏輯)
    // ==========================================
    
    public interface IVisitor
    {
        void VisitDirectory(DirectoryNode directory);
        void VisitFile(FileNode file);
        void LeaveDirectory(DirectoryNode directory);
    }

    public class SizeCalculateVisitor : IVisitor
    {
        public double TotalSize { get; private set; }
        public void VisitDirectory(DirectoryNode directory) { }
        public void VisitFile(FileNode file) => TotalSize += file.GetSize();
        public void LeaveDirectory(DirectoryNode directory) { }
    }

    public class SearchVisitor : IVisitor
    {
        private readonly string? _targetExtension;
        private readonly string? _targetName;
        public List<string> FoundPaths { get; } = new();

        public SearchVisitor(string query)
        {
            if (query.StartsWith('.'))
            {
                _targetExtension = query;
            }
            else if (Path.HasExtension(query))
            {
                _targetExtension = Path.GetExtension(query);
                _targetName = query;
            }
            else
            {
                _targetName = query;
            }
        }

        public void VisitDirectory(DirectoryNode directory) { }
        public void LeaveDirectory(DirectoryNode directory) { }
        public void VisitFile(FileNode file)
        {
            var matches = _targetExtension != null
                ? string.Equals(file.GetExtension(), _targetExtension, StringComparison.OrdinalIgnoreCase)
                : file.Name.Contains(_targetName!, StringComparison.OrdinalIgnoreCase);
            if (matches)
            {
                FoundPaths.Add(BuildPath(file));
            }
        }

        private string BuildPath(FileSystemNode node)
        {
            return NodePath.GetPath(node);
        }
    }

    public static class NodePath
    {
        public static string GetPath(FileSystemNode node)
        {
            var path = node.Name;
            var current = node.Parent;
            while (current != null)
            {
                path = current.Name + " -> " + path;
                current = current.Parent;
            }
            return path;
        }
    }

    public class ConsoleNodeObserver : INodeObserver
    {
        public void OnNodeVisited(FileSystemNode node, int currentCount, int totalCount)
        {
            Console.WriteLine($"Visiting: {NodePath.GetPath(node)}");
        }
    }

    public static class FileDetailFormatter
    {
        public static string Format(FileNode file) => file switch
        {
            WordFile word => $"頁數: {word.Pages}, 大小: {FormatSize(word.SizeKB)}",
            ImageFile image => $"解析度: {image.Width}x{image.Height}, 大小: {FormatSize(image.SizeKB)}",
            TextFile text => $"編碼: {text.Encoding}, 大小: {FormatSize(text.SizeKB)}",
            _ => $"大小: {FormatSize(file.SizeKB)}"
        };

        public static string FormatSize(double sizeKB)
        {
            if (sizeKB < 1) return $"{sizeKB * 1024:0.##}B";
            if (sizeKB >= 1024) return $"{sizeKB / 1024:0.##}MB";
            return $"{sizeKB:0.##}KB";
        }
    }

    public class TreePrintVisitor : IVisitor
    {
        public void VisitDirectory(DirectoryNode directory)
        {
            PrintNode(directory, $"{directory.Name} [目錄]");
        }

        public void VisitFile(FileNode file)
        {
            PrintNode(file, $"{file.Name} ({FileDetailFormatter.Format(file)})");
        }

        public void LeaveDirectory(DirectoryNode directory) { }

        private static void PrintNode(FileSystemNode node, string label)
        {
            if (node.Parent == null)
            {
                Console.WriteLine(label);
                return;
            }

            var ancestors = new Stack<FileSystemNode>();
            var current = node.Parent;
            while (current != null)
            {
                ancestors.Push(current);
                current = current.Parent;
            }

            ancestors.Pop();
            var prefix = new StringBuilder();
            while (ancestors.Count > 0)
            {
                var ancestor = ancestors.Pop();
                prefix.Append(IsLastChild(ancestor) ? "    " : "│   ");
            }

            var branch = IsLastChild(node) ? "└── " : "├── ";
            Console.WriteLine($"{prefix}{branch}{label}");
        }

        private static bool IsLastChild(FileSystemNode node)
        {
            var siblings = node.Parent!.Children;
            return ReferenceEquals(siblings[^1], node);
        }
    }

    public class XmlExportVisitor : IVisitor
    {
        private readonly StringBuilder _sb = new();
        private readonly XmlWriter _writer;

        public XmlExportVisitor()
        {
            _writer = XmlWriter.Create(new StringWriter(_sb), new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "    ",
                OmitXmlDeclaration = true,
                CloseOutput = false
            });
        }

        public string GetXmlString()
        {
            _writer.Flush();
            return _sb.ToString();
        }

        public void VisitDirectory(DirectoryNode directory)
        {
            _writer.WriteStartElement(SanitizeXmlName(directory.XmlName));
        }

        public void VisitFile(FileNode file)
        {
            _writer.WriteStartElement(SanitizeXmlName(file.XmlName));
            _writer.WriteString(FileDetailFormatter.Format(file));
            _writer.WriteEndElement();
        }

        public void LeaveDirectory(DirectoryNode directory) => CloseDirectory(directory);

        // 在結束目錄節點訪問時，由外部外部控制或微調
        public void CloseDirectory(DirectoryNode directory)
        {
            _writer.WriteEndElement();
        }

        private string SanitizeXmlName(string name)
        {
            var safeName = name.Replace(".", "_").Replace(" ", "_").Replace("/", "_");
            if (string.IsNullOrEmpty(safeName)) return "_";
            if (char.IsDigit(safeName[0])) safeName = "_" + safeName;
            return XmlConvert.EncodeLocalName(safeName);
        }
    }

    // ==========================================
    // 3. Command Pattern & State (命令模式與復原)
    // ==========================================
    
    public interface ICommand
    {
        string Description { get; }
        void Execute();
        void Undo();
    }

    public class TagCommand : ICommand
    {
        private readonly FileSystemNode _node;
        private readonly string _tag;
        private readonly bool _isAdd;

        public string Description => $"[Command] 執行貼上標籤({_tag}) 至 {_node.Name}";

        public TagCommand(FileSystemNode node, string tag, bool isAdd = true)
        {
            _node = node;
            _tag = tag;
            _isAdd = isAdd;
        }

        public void Execute()
        {
            if (_isAdd) _node.Tags.Add(_tag);
            else _node.Tags.Remove(_tag);
        }

        public void Undo()
        {
            Console.WriteLine($"[Undo] 恢復貼上項目: 移除標籤({_tag}) 自 {_node.Name}");
            if (_isAdd) _node.Tags.Remove(_tag);
            else _node.Tags.Add(_tag);
        }
    }

    public class DeleteCommand : ICommand
    {
        private readonly DirectoryNode _parent;
        private readonly FileSystemNode _target;

        public string Description => $"[Command] 刪除項目: {_target.Name}";

        public DeleteCommand(FileSystemNode target)
        {
            _target = target;
            _parent = target.Parent ?? throw new InvalidOperationException("Cannot delete a node without a parent directory.");
        }

        public void Execute() => _parent.Remove(_target);
        public void Undo() => _parent.Add(_target);
    }

    public class PasteCommand : ICommand
    {
        private readonly DirectoryNode _destination;
        private readonly FileSystemNode _copy;

        public string Description => $"貼上 {_copy.Name} 至 {_destination.Name}";

        public PasteCommand(FileSystemNode source, DirectoryNode destination)
        {
            _destination = destination;
            _copy = source.Clone();
            var (displayName, storageName) = CreateCopyNames(source, destination.Children);
            _copy.Name = displayName;
            _copy.StorageName = storageName;
            _copy.XmlName = displayName;
        }

        public void Execute() => _destination.Add(_copy);
        public void Undo() => _destination.Remove(_copy);

        private static (string DisplayName, string StorageName) CreateCopyNames(FileSystemNode source, IReadOnlyList<FileSystemNode> siblings)
        {
            var displayExtension = source is FileNode ? Path.GetExtension(source.Name) : string.Empty;
            var displayBase = displayExtension.Length == 0 ? source.Name : Path.GetFileNameWithoutExtension(source.Name);
            var storageExtension = source is FileNode ? Path.GetExtension(source.StorageName) : string.Empty;
            var storageBase = storageExtension.Length == 0 ? source.StorageName : Path.GetFileNameWithoutExtension(source.StorageName);
            var suffixNumber = 1;
            string displayCandidate;
            string storageCandidate;
            do
            {
                var displaySuffix = suffixNumber == 1 ? " (copy)" : $" (copy {suffixNumber})";
                var storageSuffix = suffixNumber == 1 ? "_copy" : $"_copy{suffixNumber}";
                displayCandidate = $"{displayBase}{displaySuffix}{displayExtension}";
                storageCandidate = $"{storageBase}{storageSuffix}{storageExtension}";
                suffixNumber++;
            }
            while (siblings.Any(node =>
                string.Equals(node.Name, displayCandidate, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(node.StorageName, storageCandidate, StringComparison.OrdinalIgnoreCase)));

            return (displayCandidate, storageCandidate);
        }
    }

    public class AddFileCommand : ICommand
    {
        private readonly DirectoryNode _destination;
        private readonly FileNode _file;

        public string Description => $"新增 {_file.Name} 至 {_destination.Name}";

        public AddFileCommand(DirectoryNode destination, FileNode file)
        {
            _destination = destination;
            _file = file;
        }

        public void Execute() => _destination.Add(_file);
        public void Undo() => _destination.Remove(_file);
    }

    public sealed record InspectedFile(Guid Token, DateTimeOffset ExpiresAt, string Name, string StorageName, string Type, double SizeKb, int? Pages, int? Width, int? Height, string? Encoding);
    public sealed record CreateFileRequest(string Path, Guid Token);

    public class CommandManager
    {
        private readonly Stack<ICommand> _undoStack = new();
        private readonly Stack<ICommand> _redoStack = new();

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;

        public void Execute(ICommand command)
        {
            command.Execute();
            _undoStack.Push(command);
            _redoStack.Clear();
        }

        public string? Undo()
        {
            if (!CanUndo) return null;
            var command = _undoStack.Pop();
            command.Undo();
            _redoStack.Push(command);
            return command.Description;
        }

        public string? Redo()
        {
            if (!CanRedo) return null;
            var command = _redoStack.Pop();
            command.Execute();
            _undoStack.Push(command);
            return command.Description;
        }
    }

    public class RecordingNodeObserver : INodeObserver
    {
        public List<string> VisitedPaths { get; } = new();

        public void OnNodeVisited(FileSystemNode node, int currentCount, int totalCount)
        {
            VisitedPaths.Add(NodePath.GetPath(node));
        }
    }

    public static class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            var app = builder.Build();
            var root = CreateSampleTree();
            var commandManager = new CommandManager();
            var inspectedFiles = new ConcurrentDictionary<Guid, InspectedFile>();
            FileSystemNode? clipboard = null;

            app.UseDefaultFiles();
            app.UseStaticFiles();

            app.MapGet("/api/tree", () => ToDto(root));
            app.MapGet("/api/summary", () => new
            {
                totalSizeKb = root.GetSize(),
                fileCount = EnumerateFiles(root).Count(),
                folderCount = CountDirectories(root)
            });
            app.MapGet("/api/search", (string? query) =>
            {
                if (string.IsNullOrWhiteSpace(query)) return Results.Ok(Array.Empty<object>());

                IEnumerable<FileNode> matches;
                if (query.StartsWith('.'))
                {
                    var visitor = new SearchVisitor(query);
                    var currentCount = 0;
                    root.Accept(visitor, new List<INodeObserver>(), ref currentCount, CountNodes(root));
                    var paths = new HashSet<string>(visitor.FoundPaths, StringComparer.OrdinalIgnoreCase);
                    matches = EnumerateFiles(root).Where(file => paths.Contains(NodePath.GetPath(file)));
                }
                else
                {
                    matches = EnumerateFiles(root).Where(file =>
                        file.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
                }

                return Results.Ok(matches.Select(file => new
                {
                    name = file.Name,
                    path = NodePath.GetPath(file),
                    extension = file.GetExtension(),
                    type = GetFileType(file),
                    detail = FileDetailFormatter.Format(file),
                    sizeKb = file.SizeKB
                }));
            });
            app.MapGet("/api/export/xml", () =>
            {
                var visitor = new XmlExportVisitor();
                var currentCount = 0;
                root.Accept(visitor, new List<INodeObserver>(), ref currentCount, CountNodes(root));
                return Results.Content(visitor.GetXmlString(), "application/xml; charset=utf-8");
            });
            app.MapPost("/api/visitor/size", (string? path) =>
            {
                var directory = string.IsNullOrWhiteSpace(path)
                    ? root
                    : FindNode(root, path) as DirectoryNode;
                if (directory == null) return Results.BadRequest(new { error = "請選擇要計算容量的資料夾。" });

                var visitor = new SizeCalculateVisitor();
                var observer = new RecordingNodeObserver();
                var currentCount = 0;
                var totalNodes = CountNodes(directory);
                directory.Accept(visitor, new List<INodeObserver> { observer }, ref currentCount, totalNodes);
                return Results.Ok(new
                {
                    directoryName = directory.Name,
                    totalSizeKb = visitor.TotalSize,
                    visitedPaths = observer.VisitedPaths,
                    visitedCount = currentCount,
                    totalNodes
                });
            });
            app.MapPost("/api/visitor/search", (string? query) =>
            {
                if (string.IsNullOrWhiteSpace(query)) return Results.BadRequest(new { error = "請輸入檔名或副檔名。" });

                var visitor = new SearchVisitor(query);
                var observer = new RecordingNodeObserver();
                var currentCount = 0;
                var totalNodes = CountNodes(root);
                root.Accept(visitor, new List<INodeObserver> { observer }, ref currentCount, totalNodes);
                var results = visitor.FoundPaths.Where(path =>
                    query.StartsWith('.') || Path.GetFileName(path).Contains(query, StringComparison.OrdinalIgnoreCase));
                return Results.Ok(new
                {
                    results,
                    visitedPaths = observer.VisitedPaths,
                    visitedCount = currentCount,
                    totalNodes
                });
            });
            app.MapPost("/api/commands/tag", (string path, string tag) =>
            {
                var node = FindNode(root, path);
                if (node == null) return Results.NotFound();
                var command = new TagCommand(node, tag, !node.Tags.Contains(tag));
                commandManager.Execute(command);
                return Results.Ok(new { message = command.Description, tree = ToDto(root), canUndo = commandManager.CanUndo, canRedo = commandManager.CanRedo });
            });
            app.MapPost("/api/commands/copy", (string path) =>
            {
                var node = FindNode(root, path);
                if (node == null || node.Parent == null) return Results.BadRequest(new { error = "根目錄不能複製。" });
                clipboard = node.Clone();
                return Results.Ok(new { message = $"已複製 {node.Name}", canPaste = true });
            });
            app.MapPost("/api/files/inspect", (Func<HttpRequest, Task<IResult>>)(async httpRequest =>
            {
                if (!httpRequest.HasFormContentType) return Results.BadRequest(new { error = "請選擇本機檔案。" });
                var form = await httpRequest.ReadFormAsync();
                var uploadedFile = form.Files.GetFile("file");
                if (uploadedFile == null) return Results.BadRequest(new { error = "請先選擇要檢查的檔案。" });
                if (uploadedFile.Length > 20 * 1024 * 1024) return Results.BadRequest(new { error = "檔案不可超過 20 MB。" });

                var name = Path.GetFileName(uploadedFile.FileName.Replace('\\', '/')).Trim();
                if (string.IsNullOrWhiteSpace(name) || name.Contains(" -> ")) return Results.BadRequest(new { error = "本機檔案名稱無效。" });

                try
                {
                    foreach (var stale in inspectedFiles.Where(pair => pair.Value.ExpiresAt <= DateTimeOffset.UtcNow).Select(pair => pair.Key))
                    {
                        inspectedFiles.TryRemove(stale, out _);
                    }

                    var inspected = await InspectFileAsync(name, uploadedFile);
                    inspectedFiles[inspected.Token] = inspected;
                    return Results.Ok(new
                    {
                        token = inspected.Token,
                        name = inspected.Name,
                        type = inspected.Type,
                        sizeKb = inspected.SizeKb,
                        pages = inspected.Pages,
                        width = inspected.Width,
                        height = inspected.Height,
                        encoding = inspected.Encoding
                    });
                }
                catch (InvalidDataException exception)
                {
                    return Results.BadRequest(new { error = exception.Message });
                }
            }));

            app.MapDelete("/api/files/inspect/{token:guid}", (Guid token) =>
            {
                inspectedFiles.TryRemove(token, out _);
                return Results.NoContent();
            });

            app.MapPost("/api/commands/create-file", (CreateFileRequest request) =>
            {
                var destination = FindNode(root, request.Path) as DirectoryNode;
                if (destination == null) return Results.BadRequest(new { error = "請選擇目的資料夾。" });
                if (!inspectedFiles.TryGetValue(request.Token, out var inspected) || inspected.ExpiresAt <= DateTimeOffset.UtcNow)
                {
                    inspectedFiles.TryRemove(request.Token, out _);
                    return Results.BadRequest(new { error = "檔案檢查結果已失效，請重新選擇檔案。" });
                }
                if (destination.Children.Any(child => string.Equals(child.Name, inspected.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    return Results.Conflict(new { error = "此資料夾已有相同名稱的項目。" });
                }

                FileNode file = inspected.Type switch
                {
                    "Word" => new WordFile(inspected.Name, inspected.SizeKb, inspected.Pages!.Value, inspected.StorageName),
                    "圖片" => new ImageFile(inspected.Name, inspected.SizeKb, inspected.Width!.Value, inspected.Height!.Value, inspected.StorageName),
                    "文字" => new TextFile(inspected.Name, inspected.SizeKb, inspected.Encoding!, inspected.StorageName),
                    _ => throw new InvalidOperationException("Unsupported inspected file type.")
                };

                if (!inspectedFiles.TryRemove(request.Token, out _)) return Results.BadRequest(new { error = "檔案檢查結果已使用，請重新選擇檔案。" });
                var command = new AddFileCommand(destination, file);
                commandManager.Execute(command);
                return Results.Ok(new
                {
                    message = command.Description,
                    path = file.StoragePath,
                    tree = ToDto(root),
                    canUndo = commandManager.CanUndo,
                    canRedo = commandManager.CanRedo
                });
            });
            app.MapPost("/api/commands/paste", (string path) =>
            {
                var destination = FindNode(root, path) as DirectoryNode;
                if (destination == null) return Results.BadRequest(new { error = "請先選擇目的資料夾。" });
                if (clipboard == null) return Results.BadRequest(new { error = "剪貼簿是空的。" });
                var command = new PasteCommand(clipboard, destination);
                commandManager.Execute(command);
                return Results.Ok(new { message = command.Description, tree = ToDto(root), canUndo = commandManager.CanUndo, canRedo = commandManager.CanRedo });
            });
            app.MapPost("/api/commands/delete", (string path) =>
            {
                var node = FindNode(root, path);
                if (node == null) return Results.NotFound();
                if (node.Parent == null) return Results.BadRequest(new { error = "根目錄不能刪除。" });
                var command = new DeleteCommand(node);
                commandManager.Execute(command);
                return Results.Ok(new { message = command.Description, tree = ToDto(root), canUndo = commandManager.CanUndo, canRedo = commandManager.CanRedo });
            });
            app.MapPost("/api/commands/undo", () =>
            {
                var message = commandManager.Undo();
                return message == null
                    ? Results.BadRequest(new { error = "沒有可復原的操作。" })
                    : Results.Ok(new { message = $"已復原：{message}", tree = ToDto(root), canUndo = commandManager.CanUndo, canRedo = commandManager.CanRedo });
            });
            app.MapPost("/api/commands/redo", () =>
            {
                var message = commandManager.Redo();
                return message == null
                    ? Results.BadRequest(new { error = "沒有可重做的操作。" })
                    : Results.Ok(new { message = $"已重做：{message}", tree = ToDto(root), canUndo = commandManager.CanUndo, canRedo = commandManager.CanRedo });
            });
            app.MapPost("/api/sort", (string path, string by, bool ascending = false) =>
            {
                var directory = FindNode(root, path) as DirectoryNode;
                if (directory == null) return Results.BadRequest(new { error = "請選擇資料夾以排序。" });
                directory.SortChildren(by, ascending);
                var direction = ascending ? "升冪" : "降冪";
                return Results.Ok(new { message = $"依{by}{direction}排序：{directory.Name}", tree = ToDto(root) });
            });
            app.MapFallbackToFile("index.html");

            await app.RunAsync();
        }

        private static async Task<InspectedFile> InspectFileAsync(string name, IFormFile uploadedFile)
        {
            var extension = Path.GetExtension(name).ToLowerInvariant();
            var type = extension switch
            {
                ".docx" => "Word",
                ".doc" => throw new InvalidDataException("舊式 .doc 檔不支援頁數擷取，請另存為 .docx。"),
                ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" => "圖片",
                ".txt" => "文字",
                _ => throw new InvalidDataException("目前支援 DOCX、圖片與 .txt 檔案。")
            };

            await using var uploadStream = uploadedFile.OpenReadStream();
            using var buffer = new MemoryStream();
            await uploadStream.CopyToAsync(buffer);
            var bytes = buffer.ToArray();
            var sizeKb = bytes.Length / 1024d;
            int? pages = null;
            int? width = null;
            int? height = null;
            string? encoding = null;

            if (type == "Word")
            {
                try
                {
                    using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
                    var properties = archive.GetEntry("docProps/app.xml")
                        ?? throw new InvalidDataException("DOCX 缺少頁數屬性；請先用 Word 開啟並儲存文件，再重新選取。");
                    using var propertiesStream = properties.Open();
                    var document = XDocument.Load(propertiesStream);
                    var pageValue = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "Pages")?.Value;
                    if (!int.TryParse(pageValue, out var pageCount) || pageCount <= 0)
                    {
                        throw new InvalidDataException("DOCX 尚無可讀取的頁數屬性；請先用 Word 開啟並儲存文件，再重新選取。");
                    }
                    pages = pageCount;
                }
                catch (InvalidDataException)
                {
                    throw;
                }
                catch (Exception exception) when (exception is InvalidOperationException or IOException or System.Xml.XmlException)
                {
                    throw new InvalidDataException("無法讀取 DOCX 頁數；請確認檔案完整並已由 Word 儲存。", exception);
                }
            }
            else if (type == "圖片")
            {
                try
                {
                    using var image = await Image.LoadAsync(new MemoryStream(bytes));
                    width = image.Width;
                    height = image.Height;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    throw new InvalidDataException("無法解碼圖片，請確認檔案格式與內容一致。", exception);
                }
            }
            else
            {
                encoding = DetectTextEncoding(bytes);
                if (encoding == null)
                {
                    throw new InvalidDataException("無法可靠判斷文字編碼；請使用 BOM 標記的 UTF-8／UTF-16 文字檔。");
                }
            }

            var storageName = $"file_{Guid.NewGuid():N}{Path.GetExtension(name).ToLowerInvariant()}";
            return new InspectedFile(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(10), name, storageName, type, sizeKb, pages, width, height, encoding);
        }

        private static string? DetectTextEncoding(byte[] bytes)
        {
            if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) return "UTF-8 BOM";
            if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE, 0x00, 0x00 })) return "UTF-32LE BOM";
            if (bytes.AsSpan().StartsWith(new byte[] { 0x00, 0x00, 0xFE, 0xFF })) return "UTF-32BE BOM";
            if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE })) return "UTF-16LE BOM";
            if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF })) return "UTF-16BE BOM";

            try
            {
                _ = new UTF8Encoding(false, true).GetString(bytes);
                return bytes.All(value => value < 0x80) ? "ASCII" : "UTF-8 (無 BOM)";
            }
            catch (DecoderFallbackException)
            {
                return null;
            }
        }

        private static DirectoryNode CreateSampleTree()
        {
            var root = new DirectoryNode("我的根目錄", "Root", "根目錄_Root");
            var projectDocs = new DirectoryNode("專案文件", "Project_Docs", "專案文件_Project_Docs");
            var requirements = new WordFile("需求規格書.docx", 500, 35, "requirements.docx");
            requirements.Tags.Add("Urgent");
            projectDocs.Add(requirements);
            var architecture = new ImageFile("系統架構圖.png", 2048, 1920, 1080, "architecture.png");
            architecture.Tags.Add("Work");
            projectDocs.Add(architecture);

            var personalNotes = new DirectoryNode("個人筆記", "Personal_Notes", "個人筆記_Personal_Notes");
            var tasks = new TextFile("待辦清單.txt", 1, "UTF-8", "todo.txt");
            tasks.Tags.Add("Work");
            personalNotes.Add(tasks);

            var archive = new DirectoryNode("2025備份", "Archive_2025", "Archive_2025");
            var minutes = new WordFile("舊會議記錄.docx", 200, 5, "meeting_minutes.docx");
            minutes.Tags.Add("Personal");
            archive.Add(minutes);
            personalNotes.Add(archive);

            root.Add(projectDocs);
            root.Add(personalNotes);
            root.Add(new TextFile("README.txt", 500d / 1024, "ASCII", "README.txt"));
            return root;
        }

        private static int CountNodes(DirectoryNode directory)
        {
            return 1 + directory.Children.Sum(child => child is DirectoryNode childDirectory
                ? CountNodes(childDirectory)
                : 1);
        }

        private static int CountDirectories(DirectoryNode directory)
        {
            return 1 + directory.Children.OfType<DirectoryNode>().Sum(CountDirectories);
        }

        private static IEnumerable<FileNode> EnumerateFiles(DirectoryNode directory)
        {
            foreach (var child in directory.Children)
            {
                if (child is FileNode file)
                {
                    yield return file;
                }
                else if (child is DirectoryNode childDirectory)
                {
                    foreach (var descendant in EnumerateFiles(childDirectory))
                    {
                        yield return descendant;
                    }
                }
            }
        }

        private static object ToDto(FileSystemNode node)
        {
            if (node is DirectoryNode directory)
            {
                return new
                {
                    name = directory.Name,
                    type = "directory",
                    detail = $"{directory.Children.Count} 個項目",
                    sizeKb = directory.GetSize(),
                    tags = directory.Tags.OrderBy(tag => tag).ToArray(),
                    path = directory.StoragePath,
                    displayPath = NodePath.GetPath(directory),
                    children = directory.Children.Select(ToDto).ToArray()
                };
            }

            var file = (FileNode)node;
            return new
            {
                name = file.Name,
                type = GetFileType(file),
                detail = FileDetailFormatter.Format(file),
                sizeKb = file.SizeKB,
                extension = file.GetExtension(),
                tags = file.Tags.OrderBy(tag => tag).ToArray(),
                path = file.StoragePath,
                displayPath = NodePath.GetPath(file),
                children = Array.Empty<object>()
            };
        }

        private static FileSystemNode? FindNode(DirectoryNode root, string path)
        {
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0 || !string.Equals(parts[0], root.StorageName, StringComparison.Ordinal)) return null;

            FileSystemNode current = root;
            foreach (var part in parts.Skip(1))
            {
                if (current is not DirectoryNode directory) return null;
                var child = directory.Children.FirstOrDefault(node => string.Equals(node.StorageName, part, StringComparison.Ordinal));
                if (child == null) return null;
                current = child;
            }
            return current;
        }

        private static string GetFileType(FileNode file) => file switch
        {
            WordFile => "Word",
            ImageFile => "圖片",
            TextFile => "文字",
            _ => "檔案"
        };
    }
}
