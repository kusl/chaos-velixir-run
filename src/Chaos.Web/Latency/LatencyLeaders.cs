using Chaos.Web.Messages;

namespace Chaos.Web.Latency;

internal enum LatencySide
{
    Browser,
    Server,
}

internal sealed record LatencyBoards(
    IReadOnlyList<Guid> FastJackpots,
    IReadOnlyList<Guid> SlowJackpots,
    IReadOnlyList<Guid> BrowserFastest,
    IReadOnlyList<Guid> BrowserSlowest,
    IReadOnlyList<Guid> ServerFastest,
    IReadOnlyList<Guid> ServerSlowest,
    IReadOnlyList<BoardMessage> Runs);

internal sealed class LatencyLeaders
{
    public const int FastBelow = 10;
    public const int SlowFrom = 100;

    private LatencyLeaders(
        BoardMessage[] fastJackpots,
        BoardMessage[] slowJackpots,
        BoardMessage[] browserFastest,
        BoardMessage[] browserSlowest,
        BoardMessage[] serverFastest,
        BoardMessage[] serverSlowest)
    {
        FastJackpots = fastJackpots;
        SlowJackpots = slowJackpots;
        BrowserFastest = browserFastest;
        BrowserSlowest = browserSlowest;
        ServerFastest = serverFastest;
        ServerSlowest = serverSlowest;
    }

    public static LatencyLeaders Empty { get; } = new([], [], [], [], [], []);

    public BoardMessage[] FastJackpots { get; }

    public BoardMessage[] SlowJackpots { get; }

    public BoardMessage[] BrowserFastest { get; }

    public BoardMessage[] BrowserSlowest { get; }

    public BoardMessage[] ServerFastest { get; }

    public BoardMessage[] ServerSlowest { get; }

    private IEnumerable<BoardMessage[]> Lists => [FastJackpots, SlowJackpots, BrowserFastest, BrowserSlowest, ServerFastest, ServerSlowest];

    public static int? Of(LatencyRank rank, LatencySide side) => side == LatencySide.Browser ? rank.Browser : rank.Server;

    public static bool IsFast(int? median) => median < FastBelow;

    public static bool IsSlow(int? median) => median >= SlowFrom;

    public static bool IsFastJackpot(LatencyRank rank) => IsFast(rank.Browser) || IsFast(rank.Server);

    public static bool IsSlowJackpot(LatencyRank rank) => IsSlow(rank.Browser) || IsSlow(rank.Server);

    public LatencyLeaders? Merge(IEnumerable<BoardMessage> candidates, int rankCapacity, int jackpotCapacity)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var scored = candidates.Select(Score).ToList();
        var next = new LatencyLeaders(
            Window(FastJackpots, scored.Where(entry => IsFastJackpot(entry.Rank)), jackpotCapacity),
            Window(SlowJackpots, scored.Where(entry => IsSlowJackpot(entry.Rank)), jackpotCapacity),
            Order(BrowserFastest, scored, LatencySide.Browser, fastest: true, rankCapacity),
            Order(BrowserSlowest, scored, LatencySide.Browser, fastest: false, rankCapacity),
            Order(ServerFastest, scored, LatencySide.Server, fastest: true, rankCapacity),
            Order(ServerSlowest, scored, LatencySide.Server, fastest: false, rankCapacity));
        return next.Lists.Zip(Lists).All(pair => pair.First.Select(run => run.Id).SequenceEqual(pair.Second.Select(run => run.Id))) ? null : next;
    }

    public IReadOnlyList<Guid> Entered(LatencyLeaders previous)
    {
        ArgumentNullException.ThrowIfNull(previous);
        return [.. Lists.Zip(previous.Lists)
            .SelectMany(pair => pair.First.Select(run => run.Id).Except(pair.Second.Select(run => run.Id)))
            .Distinct()];
    }

    public LatencyBoards Boards(LatencyLeaders? previous)
    {
        var known = previous?.Lists.SelectMany(list => list).Select(run => run.Id).ToHashSet() ?? [];
        return new LatencyBoards(
            Ids(FastJackpots),
            Ids(SlowJackpots),
            Ids(BrowserFastest),
            Ids(BrowserSlowest),
            Ids(ServerFastest),
            Ids(ServerSlowest),
            [.. Lists.SelectMany(list => list).DistinctBy(run => run.Id).Where(run => !known.Contains(run.Id))]);
    }

    private static Guid[] Ids(BoardMessage[] runs) => [.. runs.Select(run => run.Id)];

    private static Scored Score(BoardMessage run) => new(run, LatencyRun.Rank(run.Text));

    private static BoardMessage[] Window(BoardMessage[] current, IEnumerable<Scored> candidates, int capacity) =>
        MessageWindow.Merge(current, candidates.Select(entry => entry.Run), capacity)?.Next ?? current;

    private static BoardMessage[] Order(BoardMessage[] current, IEnumerable<Scored> candidates, LatencySide side, bool fastest, int capacity)
    {
        var pool = current.Select(Score)
            .Concat(candidates)
            .Where(entry => Of(entry.Rank, side) is not null)
            .DistinctBy(entry => entry.Run.Id);
        var sorted = fastest
            ? pool.OrderBy(entry => Of(entry.Rank, side))
            : pool.OrderByDescending(entry => Of(entry.Rank, side));
        return [.. sorted.ThenBy(entry => entry.Run.Id).Take(capacity).Select(entry => entry.Run)];
    }

    private sealed record Scored(BoardMessage Run, LatencyRank Rank);
}
