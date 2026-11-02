using System.Security.Cryptography;
using System.Text;
using Chaos.Web.Infrastructure;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace Chaos.Web.Reading;

internal sealed partial class ArticleLibrary
{
    public const long MaxFileBytes = 64L * 1024 * 1024;
    private const int Attempts = 2;

    private readonly IFileProvider _files;
    private readonly string _path;
    private readonly ILogger<ArticleLibrary> _logger;
    private readonly Lock _gate = new();
    private Shelf _shelf = Shelf.Unread;

    public ArticleLibrary(IFileProvider files, IOptions<ChaosOptions> options, ILogger<ArticleLibrary> logger)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _files = files;
        _path = options.Value.ArticlesFile;
        _logger = logger;
    }

    public int Count => Current().Entries.Count;

    public Article? Find(string? id)
    {
        if (!ArticleText.IsId(id))
        {
            return null;
        }

        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var shelf = Current();
            if (!shelf.ById.TryGetValue(id, out var entry))
            {
                return null;
            }

            if (Load(shelf, entry) is { } article)
            {
                return article;
            }
        }

        return null;
    }

    public Article? Random() => Pick(RandomNumberGenerator.GetInt32);

    public Article? Pick(Func<int, int> choose)
    {
        ArgumentNullException.ThrowIfNull(choose);
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var shelf = Current();
            if (shelf.Entries.Count == 0)
            {
                return null;
            }

            var index = choose(shelf.Entries.Count);
            if (index < 0 || index >= shelf.Entries.Count)
            {
                return null;
            }

            if (Load(shelf, shelf.Entries[index]) is { } article)
            {
                return article;
            }
        }

        return null;
    }

    private Shelf Current()
    {
        var file = _files.GetFileInfo(_path);
        var stamp = Stamp.Of(file);
        var shelf = Volatile.Read(ref _shelf);
        if (shelf.Stamp == stamp)
        {
            return shelf;
        }

        lock (_gate)
        {
            shelf = _shelf;
            if (shelf.Stamp != stamp)
            {
                shelf = Build(file, stamp);
                Volatile.Write(ref _shelf, shelf);
            }

            return shelf;
        }
    }

    private Shelf Build(IFileInfo file, Stamp stamp)
    {
        if (!stamp.Exists)
        {
            return Shelf.Empty(stamp);
        }

        if (stamp.Length > MaxFileBytes)
        {
            LogTooLarge(_logger, _path, stamp.Length);
            return Shelf.Empty(stamp);
        }

        try
        {
            var bytes = Read(file, 0, (int)stamp.Length);
            var entries = new List<ArticleEntry>();
            var byId = new Dictionary<string, ArticleEntry>(StringComparer.Ordinal);
            foreach (var range in ArticleText.Records(bytes))
            {
                var (offset, length) = range.GetOffsetAndLength(bytes.Length);
                var article = ArticleText.Parse(Encoding.UTF8.GetString(bytes, offset, length));
                if (article is null)
                {
                    continue;
                }

                var entry = new ArticleEntry(article.Id, offset, length);
                if (byId.TryAdd(entry.Id, entry))
                {
                    entries.Add(entry);
                }
            }

            return new Shelf(stamp, entries, byId);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogUnreadable(_logger, _path, exception);
            return Shelf.Unread;
        }
    }

    private Article? Load(Shelf shelf, ArticleEntry entry)
    {
        try
        {
            var file = _files.GetFileInfo(_path);
            if (Stamp.Of(file) != shelf.Stamp)
            {
                return null;
            }

            var article = ArticleText.Parse(Encoding.UTF8.GetString(Read(file, entry.Offset, entry.Length)));
            if (article?.Id == entry.Id)
            {
                return article;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogUnreadable(_logger, _path, exception);
        }

        Interlocked.CompareExchange(ref _shelf, Shelf.Unread, shelf);
        return null;
    }

    private static byte[] Read(IFileInfo file, long offset, int length)
    {
        using var stream = file.CreateReadStream();
        if (stream.CanSeek)
        {
            stream.Seek(offset, SeekOrigin.Begin);
        }
        else
        {
            var skip = new byte[Math.Min(81_920, Math.Max(1L, offset))];
            for (var left = offset; left > 0;)
            {
                var read = stream.Read(skip, 0, (int)Math.Min(skip.Length, left));
                if (read == 0)
                {
                    throw new EndOfStreamException();
                }

                left -= read;
            }
        }

        var bytes = new byte[length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Articles file {Path} is {Length} bytes, over the limit; no articles are shown")]
    private static partial void LogTooLarge(ILogger logger, string path, long length);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Articles file {Path} could not be read")]
    private static partial void LogUnreadable(ILogger logger, string path, Exception exception);

    private readonly record struct Stamp(bool Exists, long Length, DateTimeOffset Modified)
    {
        public static readonly Stamp None = new(false, -2, DateTimeOffset.MinValue);

        public static Stamp Of(IFileInfo file) =>
            file.Exists && !file.IsDirectory ? new Stamp(true, file.Length, file.LastModified) : new Stamp(false, -1, DateTimeOffset.MinValue);
    }

    private sealed record Shelf(Stamp Stamp, IReadOnlyList<ArticleEntry> Entries, IReadOnlyDictionary<string, ArticleEntry> ById)
    {
        public static readonly Shelf Unread = Empty(Stamp.None);

        public static Shelf Empty(Stamp stamp) => new(stamp, [], new Dictionary<string, ArticleEntry>(StringComparer.Ordinal));
    }
}
