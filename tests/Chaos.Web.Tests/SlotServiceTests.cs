using Chaos.Web.Infrastructure;
using Chaos.Web.Messages;
using Chaos.Web.Slots;

namespace Chaos.Web.Tests;

public sealed class SlotServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
    private readonly ManualTimeProvider _time = new(Start);

    [Fact]
    public void PressStartsASpinStampedWithTheServerClock()
    {
        var slots = Create();
        var press = slots.Press("a");
        Assert.Equal(PressResult.Accepted, press.Result);
        var result = Assert.Single(slots.Messages);
        Assert.Equal(press.Id, result.Id);
        Assert.True(SlotRules.IsValid(result.Text));
        Assert.Equal(Start, Uuid7.Timestamp(result.Id));
        Assert.Equal(press.Id.ToString(), press.Reply);
    }

    [Fact]
    public void NobodyCanPressUntilTheSpinSettles()
    {
        var slots = Create();
        Assert.Equal(PressResult.Accepted, slots.Press("a").Result);
        Assert.Equal(PressResult.Busy, slots.Press("b").Result);
        Assert.Equal(PressResult.Busy, slots.Press("a").Result);
        Assert.Equal("busy", slots.Press("b").Reply);
        _time.Advance(slots.SpinTime - TimeSpan.FromMilliseconds(1));
        Assert.Equal(PressResult.Busy, slots.Press("b").Result);
        _time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(PressResult.Accepted, slots.Press("b").Result);
    }

    [Fact]
    public void BusyPressesDoNotSpendTheClientsAllowance()
    {
        var slots = Create(o =>
        {
            o.PressBurst = 1;
            o.PressesPerSecond = 0.01;
        });
        Assert.Equal(PressResult.Accepted, slots.Press("a").Result);
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(PressResult.Busy, slots.Press("b").Result);
        }

        _time.Advance(slots.SpinTime);
        Assert.Equal(PressResult.Accepted, slots.Press("b").Result);
    }

    [Fact]
    public void PressIsRateLimitedPerClient()
    {
        var slots = Create(o =>
        {
            o.PressBurst = 1;
            o.PressesPerSecond = 0.1;
        });
        Assert.Equal(PressResult.Accepted, slots.Press("a").Result);
        _time.Advance(slots.SpinTime);
        var press = slots.Press("a");
        Assert.Equal(PressResult.TooFast, press.Result);
        Assert.Equal("slow", press.Reply);
        Assert.Equal(PressResult.Accepted, slots.Press("b").Result);
    }

    [Fact]
    public void SpinningReportsTheTimeLeft()
    {
        var slots = Create(o => o.SpinMilliseconds = 4000);
        Assert.Null(slots.Spinning);
        var press = slots.Press("a");
        _time.Advance(TimeSpan.FromMilliseconds(1500));
        var spin = Assert.IsType<SlotSpin>(slots.Spinning);
        Assert.Equal(press.Id, spin.Id);
        Assert.Equal(2500, spin.Remaining);
        _time.Advance(TimeSpan.FromMilliseconds(2500));
        Assert.Null(slots.Spinning);
    }

    [Fact]
    public void SubscribersSeeTheSpinAndLateJoinersSeeWhatIsLeft()
    {
        var slots = Create(o => o.SpinMilliseconds = 4000);
        var early = new SlotRecorder();
        slots.Subscribe(early);
        var press = slots.Press("a");

        var delta = early.Updates[1];
        Assert.Equal("delta", delta.Kind);
        Assert.Equal(press.Id, Assert.Single(delta.Added).Id);
        Assert.Equal(new SlotSpin(press.Id, 4000), delta.Spin);

        _time.Advance(TimeSpan.FromSeconds(1));
        var late = new SlotRecorder();
        slots.Subscribe(late);
        var snapshot = Assert.Single(late.Updates);
        Assert.Equal("snapshot", snapshot.Kind);
        Assert.Equal(new SlotSpin(press.Id, 3000), snapshot.Spin);

        _time.Advance(TimeSpan.FromSeconds(3));
        var settled = new SlotRecorder();
        slots.Subscribe(settled);
        Assert.Null(Assert.Single(settled.Updates).Spin);
    }

    [Fact]
    public void KeepsOnlyTheNewestResults()
    {
        var slots = Create(o => o.SlotCapacity = 2);
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            ids.Add(slots.Press($"c{i}").Id);
            _time.Advance(slots.SpinTime);
        }

        Assert.Equal([ids[2], ids[1]], slots.Messages.Select(m => m.Id));
    }

    [Fact]
    public void OfferAcceptsOlderValidResults()
    {
        var slots = Create();
        var recorder = new SlotRecorder();
        slots.Subscribe(recorder);
        Assert.Equal(2, slots.Offer([Old(-2, "ABCDE"), Old(-1, "ZZZ12")]));
        Assert.Equal(["ZZZ12", "ABCDE"], slots.Messages.Select(m => m.Text));
        Assert.Null(recorder.Updates[1].Spin);
    }

    [Fact]
    public void OfferRejectsForgedResults()
    {
        var slots = Create();
        _time.Advance(TimeSpan.FromHours(1));
        Assert.Equal(0, slots.Offer(
        [
            new BoardMessage(Guid.NewGuid(), "ABCDE"),
            new BoardMessage(Guid.CreateVersion7(Start.AddMinutes(5)), "ABCDE"),
            new BoardMessage(Guid.CreateVersion7(Start.AddHours(2)), "ABCDE"),
            Old(-1, "abcde"),
            Old(-2, "ABCDEF"),
            Old(-3, "hello world"),
        ]));
        Assert.Empty(slots.Messages);
    }

    [Fact]
    public void OfferConsidersAtMostFiftyResults()
    {
        var slots = Create(o => o.SlotCapacity = 100);
        var offered = Enumerable.Range(1, 60).Select(i => Old(-i, "ABCDE")).ToList();
        Assert.Equal(SlotService.MaxOffers, slots.Offer(offered));
    }

    [Fact]
    public void WinnersOutliveTheRecentResults()
    {
        var slots = Create(o => o.SlotCapacity = 2);
        Assert.Equal(3, slots.Offer([Old(-3, "AAAB1"), Old(-2, "ZK8A0"), Old(-1, "ZK8A1")]));
        Assert.Equal(["ZK8A1", "ZK8A0"], slots.Messages.Select(m => m.Text));
        Assert.Equal("AAAB1", Assert.Single(slots.Winners).Text);
    }

    [Fact]
    public void KeepsOnlyTheNewestWinners()
    {
        var slots = Create(o => o.SlotWinnerCapacity = 2);
        slots.Offer([Old(-3, "AAAB1"), Old(-2, "13AKZ"), Old(-1, "ZKA80"), Old(-4, "ZK8A0")]);
        Assert.Equal(["ZKA80", "13AKZ"], slots.Winners.Select(m => m.Text));
    }

    [Fact]
    public void OfferedWinnersSkipTheRecentResults()
    {
        var slots = Create();
        var recorder = new SlotRecorder();
        slots.Subscribe(recorder);
        Assert.Equal(1, slots.OfferWinners([Old(-2, "ZK8A0"), Old(-1, "KKKKK")]));
        Assert.Empty(slots.Messages);
        Assert.Equal("KKKKK", Assert.Single(slots.Winners).Text);
        var delta = recorder.Updates[1];
        Assert.Empty(delta.Added);
        Assert.Equal("KKKKK", Assert.Single(delta.WinnersAdded).Text);
        Assert.Equal(0, slots.OfferWinners([Old(-2, "ZK8A0")]));
    }

    [Fact]
    public void DeltasReportWinnersThatFallOff()
    {
        var slots = Create(o => o.SlotWinnerCapacity = 1);
        var first = Old(-2, "AAAB1");
        slots.Offer([first]);
        var recorder = new SlotRecorder();
        slots.Subscribe(recorder);
        Assert.Equal(first.Id, Assert.Single(recorder.Updates[0].WinnersAdded).Id);
        slots.Offer([Old(-1, "13AKZ")]);
        Assert.Equal(first.Id, Assert.Single(recorder.Updates[1].WinnersRemoved));
    }

    [Fact]
    public void WinnerOffersConsiderFiveHundred()
    {
        var slots = Create(o => o.SlotWinnerCapacity = 1000);
        var offered = Enumerable.Range(1, 600).Select(i => Old(-i, "13AKZ")).ToList();
        Assert.Equal(SlotService.MaxWinnerOffers, slots.OfferWinners(offered));
        Assert.Equal(500, slots.Winners.Count);
    }

    [Fact]
    public void JackpotsOutliveEveryOtherList()
    {
        var slots = Create(o =>
        {
            o.SlotCapacity = 1;
            o.SlotWinnerCapacity = 1;
        });
        slots.Offer([Old(-5, "XXXXB"), Old(-4, "KKKKK"), Old(-3, "13AKZ"), Old(-2, "AAAB1"), Old(-1, "ZK8A0")]);
        Assert.Equal(["ZK8A0"], slots.Messages.Select(m => m.Text));
        Assert.Equal(["AAAB1"], slots.Winners.Select(m => m.Text));
        Assert.Equal(["KKKKK", "XXXXB"], slots.Jackpots.Select(m => m.Text));
    }

    [Fact]
    public void OfferedJackpotsOnlyTouchTheJackpots()
    {
        var slots = Create();
        var recorder = new SlotRecorder();
        slots.Subscribe(recorder);
        var jackpot = Old(-1, "QQQQ7");
        Assert.Equal(1, slots.OfferJackpots([Old(-3, "AAAB1"), Old(-2, "13AKZ"), jackpot]));
        Assert.Empty(slots.Messages);
        Assert.Empty(slots.Winners);
        Assert.Equal("QQQQ7", Assert.Single(slots.Jackpots).Text);
        var delta = recorder.Updates[1];
        Assert.Empty(delta.Added);
        Assert.Empty(delta.WinnersAdded);
        Assert.Equal("QQQQ7", Assert.Single(delta.JackpotsAdded).Text);
        Assert.Equal(0, slots.OfferJackpots([jackpot]));
    }

    [Fact]
    public void SequencesAreKeptAsJackpots()
    {
        var slots = Create();
        Assert.Equal(2, slots.OfferJackpots([Old(-3, "13AKZ"), Old(-2, "ABCDE"), Old(-1, "43210")]));
        Assert.Equal(["43210", "ABCDE"], slots.Jackpots.Select(m => m.Text));
    }

    [Fact]
    public void OfferedJackpotsStopAtCapacityWithoutEvicting()
    {
        var slots = Create(o => o.SlotJackpotCapacity = 2);
        var oldest = Old(-9, "KKKKK");
        Assert.Equal(2, slots.OfferJackpots([oldest, Old(-8, "XXXXB")]));
        Assert.Equal(0, slots.OfferJackpots([Old(-1, "77777")]));
        Assert.Contains(slots.Jackpots, m => m.Id == oldest.Id);
        Assert.Equal(2, slots.Jackpots.Count);
    }

    [Fact]
    public void DrawnJackpotsAreAlwaysKept()
    {
        var slots = Create(o =>
        {
            o.SlotDrawSymbols = "7";
            o.SlotCapacity = 1;
            o.SlotWinnerCapacity = 1;
            o.SlotJackpotCapacity = 1;
        });
        var recorder = new SlotRecorder();
        slots.Subscribe(recorder);
        var first = slots.Press("a");
        _time.Advance(slots.SpinTime);
        var second = slots.Press("a");
        Assert.Equal("77777", slots.Messages[0].Text);
        Assert.Equal([second.Id, first.Id], slots.Jackpots.Select(m => m.Id));
        Assert.Equal(second.Id, Assert.Single(recorder.Updates[^1].JackpotsAdded).Id);
        Assert.Equal(second.Id, recorder.Updates[^1].Spin!.Id);
    }

    [Fact]
    public void SnapshotsCarryEveryJackpot()
    {
        var slots = Create(o => o.SlotWinnerCapacity = 1);
        slots.Offer([Old(-2, "XXXXB"), Old(-1, "KKKKK")]);
        var recorder = new SlotRecorder();
        slots.Subscribe(recorder);
        var snapshot = Assert.Single(recorder.Updates);
        Assert.Single(snapshot.WinnersAdded);
        Assert.Equal(["KKKKK", "XXXXB"], snapshot.JackpotsAdded.Select(m => m.Text));
    }

    [Fact]
    public void AnInvalidDrawAlphabetFallsBackToEverySymbol()
    {
        var slots = Create(o => o.SlotDrawSymbols = "abc");
        slots.Press("a");
        Assert.True(SlotRules.IsValid(slots.Messages[0].Text));
    }

    [Fact]
    public void SubscribersThatCannotKeepUpAreDropped()
    {
        var slots = Create();
        var recorder = new SlotRecorder(accept: false);
        slots.Subscribe(recorder);
        Assert.False(slots.IsSubscribed(recorder));
    }

    private SlotService Create(Action<ChaosOptions>? configure = null) => new(_time, Limits.Create(configure));

    private static BoardMessage Old(int minutes, string text) =>
        new(Guid.CreateVersion7(Start.AddMinutes(minutes)), text);
}
