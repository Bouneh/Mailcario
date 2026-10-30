using System.IO;
using System.Text.Json;

namespace Mailcario;

public sealed class MailTemplate
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public string Html { get; set; } = "";
}

public sealed class MailTemplateStore
{
    private readonly string _path;
    public MailTemplateStore(string? path = null) => _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mailcario", "templates.json");

    public IReadOnlyList<MailTemplate> List()
    {
        try { return File.Exists(_path) ? JsonSerializer.Deserialize<List<MailTemplate>>(File.ReadAllText(_path)) ?? [] : []; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }

    public void Save(MailTemplate template)
    {
        var all = List().ToList();
        all.RemoveAll(item => item.Id == template.Id);
        all.Add(template);
        Write(all);
    }

    public void Delete(string id)
    {
        var all = List().ToList();
        all.RemoveAll(item => item.Id == id);
        Write(all);
    }

    private void Write(List<MailTemplate> templates)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(templates, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, _path, true);
    }
}
