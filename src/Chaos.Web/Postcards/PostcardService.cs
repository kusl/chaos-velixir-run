using Chaos.Web.Infrastructure;
using Chaos.Web.Messages;
using Microsoft.Extensions.Options;

namespace Chaos.Web.Postcards;

internal sealed class PostcardService : IDisposable
{
    private readonly TimeProvider _time;
    private readonly KeyedTokenBuckets _posts;
    private readonly SemaphoreSlim _uploads;
    private readonly Lock _gate = new();
    private readonly HashSet<IPostcardSubscriber> _subscribers = [];
    private Stored? _current;
    private long _version;

    public PostcardService(TimeProvider time, IOptions<ChaosOptions> options)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(options);
        var limits = options.Value;
        _time = time;
        _posts = new KeyedTokenBuckets(limits.PostcardBurst, limits.PostcardsPerSecond, time);
        _uploads = new SemaphoreSlim(limits.PostcardUploads, limits.PostcardUploads);
        StartedAt = time.GetUtcNow();
    }

    public DateTimeOffset StartedAt { get; }

    public Postcard? Current => Volatile.Read(ref _current)?.Card;

    public long Version
    {
        get
        {
            lock (_gate)
            {
                return _version;
            }
        }
    }

    public void Dispose() => _uploads.Dispose();

    public static PostcardResult Check(string? text, IReadOnlyList<long> lengths)
    {
        ArgumentNullException.ThrowIfNull(lengths);
        if (lengths.Count > PostcardRules.MaxImages)
        {
            return PostcardResult.TooManyImages;
        }

        long total = 0;
        foreach (var length in lengths)
        {
            if (length is <= 0 or > PostcardRules.MaxImageBytes)
            {
                return PostcardResult.TooLarge;
            }

            total += length;
        }

        if (total > PostcardRules.MaxTotalBytes)
        {
            return PostcardResult.TooLarge;
        }

        return lengths.Count == 0 && MessageText.Normalize(text, PostcardRules.MaxTextLength).Length == 0
            ? PostcardResult.Empty
            : PostcardResult.Accepted;
    }

    public PostcardResult Admit(string? text, IReadOnlyList<long> lengths, string client)
    {
        var check = Check(text, lengths);
        if (check is not PostcardResult.Accepted)
        {
            return check;
        }

        return _posts.TryTake(client) ? PostcardResult.Accepted : PostcardResult.TooFast;
    }

    public IDisposable? TryBeginUpload() => _uploads.Wait(0) ? new UploadLease(_uploads) : null;

    public PostcardReply Submit(string? text, IReadOnlyList<byte[]> images, IEnumerable<MetaEntry?>? meta, string client)
    {
        ArgumentNullException.ThrowIfNull(images);
        var admitted = Admit(text, [.. images.Select(image => (long)image.Length)], client);
        return admitted is PostcardResult.Accepted ? Publish(text, images, meta) : new PostcardReply(admitted);
    }

    public PostcardReply Publish(string? input, IReadOnlyList<byte[]> images, IEnumerable<MetaEntry?>? meta)
    {
        ArgumentNullException.ThrowIfNull(images);
        var text = MessageText.Normalize(input, PostcardRules.MaxTextLength);
        var check = Check(text, [.. images.Select(image => (long)image.Length)]);
        if (check is not PostcardResult.Accepted)
        {
            return new PostcardReply(check);
        }

        var clean = Clean(images);
        if (clean is null)
        {
            return new PostcardReply(PostcardResult.BadImage);
        }

        lock (_gate)
        {
            if (_current is { } current && current.Matches(text, clean))
            {
                return new PostcardReply(PostcardResult.Repeat);
            }

            var id = Uuid7.After(_current?.Card.Id ?? Guid.Empty, _time.GetUtcNow());
            var card = new Postcard(id, text, [.. clean.Select(image => image.Info)], MessageMeta.Normalize(meta));
            Replace(new Stored(card, clean));
            return new PostcardReply(PostcardResult.Accepted, card);
        }
    }

    public bool WouldRestore(Guid id)
    {
        if (!MessageWindow.IsRestorable(id, _time.GetUtcNow(), StartedAt))
        {
            return false;
        }

        lock (_gate)
        {
            return _current is null || _current.Card.Id.CompareTo(id) < 0;
        }
    }

    public bool Offer(Guid id, string? text, IReadOnlyList<byte[]> images, IReadOnlyList<MetaEntry>? meta)
    {
        ArgumentNullException.ThrowIfNull(images);
        text ??= "";
        if (!WouldRestore(id)
            || (text.Length > 0 && !MessageText.IsNormalized(text, PostcardRules.MaxTextLength))
            || Check(text, [.. images.Select(image => (long)image.Length)]) is not PostcardResult.Accepted
            || !MessageMeta.IsNormalized(meta))
        {
            return false;
        }

        var clean = Clean(images);
        if (clean is null)
        {
            return false;
        }

        lock (_gate)
        {
            if (_current is not null && _current.Card.Id.CompareTo(id) >= 0)
            {
                return false;
            }

            Replace(new Stored(new Postcard(id, text, [.. clean.Select(image => image.Info)], meta is { Count: > 0 } ? [.. meta] : null), clean));
            return true;
        }
    }

    public bool TryGetImage(Guid id, int index, out PostcardImage? info, out ReadOnlyMemory<byte> bytes)
    {
        var current = Volatile.Read(ref _current);
        if (current is not null && current.Card.Id == id && index >= 0 && index < current.Images.Length)
        {
            info = current.Images[index].Info;
            bytes = current.Images[index].Bytes;
            return true;
        }

        info = null;
        bytes = default;
        return false;
    }

    public void Subscribe(IPostcardSubscriber subscriber)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        lock (_gate)
        {
            if (subscriber.Deliver(PostcardUpdate.Snapshot(_version, _current?.Card)))
            {
                _subscribers.Add(subscriber);
            }
        }
    }

    public void Unsubscribe(IPostcardSubscriber subscriber)
    {
        lock (_gate)
        {
            _subscribers.Remove(subscriber);
        }
    }

    public bool IsSubscribed(IPostcardSubscriber subscriber)
    {
        lock (_gate)
        {
            return _subscribers.Contains(subscriber);
        }
    }

    private static CleanImage[]? Clean(IReadOnlyList<byte[]> images)
    {
        var clean = new CleanImage[images.Count];
        for (var i = 0; i < images.Count; i++)
        {
            if (images[i] is not { } bytes || ImageSanitizer.Clean(bytes) is not { } image)
            {
                return null;
            }

            clean[i] = image;
        }

        return clean;
    }

    private void Replace(Stored next)
    {
        _version++;
        Volatile.Write(ref _current, next);
        var update = PostcardUpdate.Change(_version, next.Card);
        List<IPostcardSubscriber>? dropped = null;
        foreach (var subscriber in _subscribers)
        {
            if (!subscriber.Deliver(update))
            {
                (dropped ??= []).Add(subscriber);
            }
        }

        if (dropped is not null)
        {
            _subscribers.ExceptWith(dropped);
        }
    }

    private sealed record Stored(Postcard Card, CleanImage[] Images)
    {
        public bool Matches(string text, CleanImage[] images) =>
            string.Equals(Card.Text, text, StringComparison.Ordinal)
            && Images.Length == images.Length
            && Images.Zip(images).All(pair => pair.First.Bytes.AsSpan().SequenceEqual(pair.Second.Bytes));
    }

    private sealed class UploadLease(SemaphoreSlim gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                gate.Release();
            }
        }
    }
}
