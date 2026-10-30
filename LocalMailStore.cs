using System.IO;
using System.Text.Json;

namespace Mailcario;

public sealed class LocalMailFolder
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

public sealed class LocalMailRecord
{
    public string Id { get; set; } = "";
    public string FolderId { get; set; } = "";
    public string SourceAccount { get; set; } = "";
    public string SourceFolder { get; set; } = "";
    public uint SourceUid { get; set; }
    public string From { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public string To { get; set; } = "";
    public string Cc { get; set; } = "";
    public string Subject { get; set; } = "";
    public string MessageId { get; set; } = "";
    public string InReplyTo { get; set; } = "";
    public string References { get; set; } = "";
    public DateTimeOffset? Date { get; set; }
    public bool IsRead { get; set; }
    public bool IsFlagged { get; set; }

    public MailItem ToMailItem() => new()
    {
        LocalId = Id, AccountEmail = SourceAccount, FolderName = SourceFolder,
        From = From, FromAddress = FromAddress, To = To, Cc = Cc,
        Subject = Subject, MessageId = MessageId, InReplyTo = InReplyTo,
        References = References, Date = Date, IsRead = IsRead,
        IsFlagged = IsFlagged
    };
}

public sealed class LocalMailStore
{
    private readonly string _root;
    private readonly string _catalogPath;
    private readonly string _messagesPath;
    private readonly object _gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public LocalMailStore(string? root = null)
    {
        _root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Mailcario", "LocalFolders");
        _catalogPath = Path.Combine(_root, "catalog.json");
        _messagesPath = Path.Combine(_root, "Messages");
    }

    public IReadOnlyList<LocalMailFolder> ListFolders()
    {
        lock (_gate) return Load().Folders.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public LocalMailFolder CreateFolder(string name)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 100 || name.Any(char.IsControl))
            throw new ArgumentException("Enter a folder name of 1 to 100 characters.", nameof(name));
        lock (_gate)
        {
            var catalog = Load();
            if (catalog.Folders.Any(f => f.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase)))
                throw new InvalidOperationException("A local folder already has this name.");
            var folder = new LocalMailFolder { Id = Guid.NewGuid().ToString("N"), Name = name };
            catalog.Folders.Add(folder);
            Save(catalog);
            return folder;
        }
    }

    public void RenameFolder(string folderId, string name)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 100 || name.Any(char.IsControl))
            throw new ArgumentException("Enter a folder name of 1 to 100 characters.", nameof(name));
        lock (_gate)
        {
            var catalog = Load();
            var folder = catalog.Folders.FirstOrDefault(f => f.Id == folderId) ??
                throw new DirectoryNotFoundException("Local folder not found.");
            if (catalog.Folders.Any(f => f.Id != folderId && f.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase)))
                throw new InvalidOperationException("A local folder already has this name.");
            folder.Name = name;
            Save(catalog);
        }
    }

    public void DeleteFolder(string folderId)
    {
        lock (_gate)
        {
            var catalog = Load();
            if (catalog.Folders.RemoveAll(f => f.Id == folderId) == 0) return;
            var removed = catalog.Messages.Where(m => m.FolderId == folderId).Select(m => m.Id).ToList();
            catalog.Messages.RemoveAll(m => m.FolderId == folderId);
            Save(catalog);
            foreach (string id in removed) File.Delete(MessagePath(id));
        }
    }

    public IReadOnlyList<LocalMailRecord> ListMessages(string folderId)
    {
        lock (_gate) return Load().Messages.Where(m => m.FolderId == folderId).ToList();
    }

    public LocalMailRecord CopyMessage(string folderId, string sourceAccount, string sourceFolder,
        MailItem message, byte[] raw)
    {
        if (raw.Length == 0) throw new ArgumentException("The message is empty.", nameof(raw));
        lock (_gate)
        {
            var catalog = Load();
            if (!catalog.Folders.Any(f => f.Id == folderId)) throw new DirectoryNotFoundException("Local folder not found.");
            string id = Guid.NewGuid().ToString("N");
            var record = new LocalMailRecord
            {
                Id = id, FolderId = folderId, SourceAccount = sourceAccount, SourceFolder = sourceFolder,
                SourceUid = message.Uid, From = message.From, FromAddress = message.FromAddress,
                To = message.To, Cc = message.Cc, Subject = message.Subject,
                MessageId = message.MessageId, InReplyTo = message.InReplyTo,
                References = message.References, Date = message.Date,
                IsRead = message.IsRead, IsFlagged = message.IsFlagged
            };
            Directory.CreateDirectory(_messagesPath);
            string path = MessagePath(id);
            string temp = path + ".tmp";
            try
            {
                File.WriteAllBytes(temp, raw);
                File.Move(temp, path);
                catalog.Messages.Add(record);
                Save(catalog);
                return record;
            }
            catch
            {
                if (File.Exists(temp)) File.Delete(temp);
                if (File.Exists(path)) File.Delete(path);
                throw;
            }
        }
    }

    public MailContent LoadMessage(string id)
    {
        lock (_gate)
        {
            if (!Load().Messages.Any(m => m.Id == id)) throw new FileNotFoundException("Local message not found.");
            return MimeReader.ReadMessage(File.ReadAllBytes(MessagePath(id)));
        }
    }

    public void SetRead(string id, bool read)
    {
        lock (_gate)
        {
            var catalog = Load();
            var record = catalog.Messages.FirstOrDefault(m => m.Id == id) ??
                throw new FileNotFoundException("Local message not found.");
            record.IsRead = read;
            Save(catalog);
        }
    }

    public void DeleteMessage(string id)
    {
        lock (_gate)
        {
            var catalog = Load();
            if (catalog.Messages.RemoveAll(m => m.Id == id) == 0) return;
            Save(catalog);
            File.Delete(MessagePath(id));
        }
    }

    private string MessagePath(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("Invalid local message identifier.", nameof(id));
        return Path.Combine(_messagesPath, id + ".eml");
    }

    private Catalog Load()
    {
        if (!File.Exists(_catalogPath)) return new Catalog();
        return JsonSerializer.Deserialize<Catalog>(File.ReadAllText(_catalogPath)) ?? new Catalog();
    }

    private void Save(Catalog catalog)
    {
        Directory.CreateDirectory(_root);
        string temp = _catalogPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(catalog, JsonOptions));
        File.Move(temp, _catalogPath, true);
    }

    private sealed class Catalog
    {
        public List<LocalMailFolder> Folders { get; set; } = [];
        public List<LocalMailRecord> Messages { get; set; } = [];
    }
}
