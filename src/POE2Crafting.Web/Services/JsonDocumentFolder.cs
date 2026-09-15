using System.Text.Json;
using System.Text.Json.Serialization;

namespace POE2Crafting.Web.Services;

/// <summary>
/// Documents of one type as one JSON file each (id.json) in a folder: atomic writes, unreadable files are skipped with a warning.
/// Not synchronised — the owning store serialises access.
/// </summary>
public sealed class JsonDocumentFolder<T> where T : class
{
    private const string Extension = ".json", TempExtension = ".json.tmp";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _folder;
    private readonly ILogger _logger;

    public JsonDocumentFolder(string folder, ILogger logger)
    {
        _folder = folder;
        _logger = logger;
        Directory.CreateDirectory(folder);
        // a crash between writing and renaming leaves a temp file behind
        foreach (var stale in Directory.EnumerateFiles(folder, "*" + TempExtension)) TryDelete(stale);
    }

    /// <summary>Every readable document of the folder.</summary>
    public IEnumerable<T> ReadAll() => Directory.EnumerateFiles(_folder, "*" + Extension).Select(TryRead).OfType<T>();

    /// <summary>The document, or null when it is missing or can't be read.</summary>
    public T? Read(string id) => TryRead(PathOf(id));

    /// <summary>Write the document atomically (temp file, flushed, then renamed).</summary>
    /// <exception cref="IOException">The file could not be written.</exception>
    public void Write(string id, T document)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        var path = PathOf(id);
        try
        {
            using (var stream = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(json);
                stream.Flush(flushToDisk: true);
            }
            File.Move(path + ".tmp", path, overwrite: true);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new IOException($"The file {path} is not writable.", ex);
        }
    }

    public void Delete(string id)
    {
        TryDelete(PathOf(id));
        TryDelete(PathOf(id) + ".tmp");
    }

    private string PathOf(string id) => Path.Combine(_folder, Path.GetFileName(id) + Extension);

    /// <summary>A document, or null when the file is missing, locked or not valid: one bad file never breaks a list.</summary>
    private T? TryRead(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllBytes(path), JsonOptions) : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger.LogWarning(ex, "Skipping unreadable file {Path}", path);
            return null;
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not delete {Path}", path);
        }
    }
}
