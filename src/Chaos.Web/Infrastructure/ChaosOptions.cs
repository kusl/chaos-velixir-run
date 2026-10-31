using Chaos.Web.Drawing;
using Chaos.Web.Slots;

namespace Chaos.Web.Infrastructure;

internal sealed class ChaosOptions
{
    public const string SectionName = "Chaos";

    public IList<string> AllowedOrigins { get; } = [];

    public int MaxConnectionsPerClient { get; set; } = 8;

    public int MaxConnections { get; set; } = 80;

    public int RequestsPerSecond { get; set; } = 10;

    public int RequestBurst { get; set; } = 120;

    public int BoardCapacity { get; set; } = 50;

    public double PostBurst { get; set; } = 3;

    public double PostsPerSecond { get; set; } = 0.5;

    public double PostcardBurst { get; set; } = 2;

    public double PostcardsPerSecond { get; set; } = 0.1;

    public int PostcardUploads { get; set; } = 2;

    public int CanvasMaxPoints { get; set; } = 20_000;

    public double StrokePointBurst { get; set; } = 4_000;

    public double StrokePointsPerSecond { get; set; } = 1_500;

    public double ResetBurst { get; set; } = 2;

    public double ResetsPerSecond { get; set; } = 0.2;

    public int SlotCapacity { get; set; } = 50;

    public int SlotWinnerCapacity { get; set; } = 500;

    public int SlotJackpotCapacity { get; set; } = 10_000;

    public string SlotDrawSymbols { get; set; } = SlotRules.Symbols;

    public double PressBurst { get; set; } = 3;

    public double PressesPerSecond { get; set; } = 0.5;

    public int SpinMilliseconds { get; set; } = 4000;

    public int LatencyCapacity { get; set; } = 50;

    public int LatencyRankCapacity { get; set; } = 10;

    public int LatencyJackpotCapacity { get; set; } = 50;

    public int LatencyCooldownSeconds { get; set; } = 300;

    public int LatencyGapSeconds { get; set; } = 10;

    public int LatencyBudgetSeconds { get; set; } = 30;

    public bool LatencyServerProbe { get; set; } = true;

    public bool IsValid() =>
        MaxConnectionsPerClient > 0
        && MaxConnections >= MaxConnectionsPerClient
        && RequestsPerSecond > 0
        && RequestBurst > 0
        && BoardCapacity > 0
        && PostBurst >= 1
        && PostsPerSecond > 0
        && PostcardBurst >= 1
        && PostcardsPerSecond > 0
        && PostcardUploads is >= 1 and <= 16
        && CanvasMaxPoints >= CanvasService.MaxStrokePoints
        && StrokePointBurst >= CanvasService.MaxBatchPoints
        && StrokePointsPerSecond > 0
        && ResetBurst >= 1
        && ResetsPerSecond > 0
        && SlotCapacity > 0
        && SlotWinnerCapacity > 0
        && SlotJackpotCapacity > 0
        && SlotRules.IsAlphabet(SlotDrawSymbols)
        && PressBurst >= 1
        && PressesPerSecond > 0
        && SpinMilliseconds is >= 1000 and <= 15000
        && LatencyCapacity > 0
        && LatencyRankCapacity is >= 1 and <= 100
        && LatencyJackpotCapacity is >= 1 and <= 1_000
        && LatencyCooldownSeconds is >= 1 and <= 86_400
        && LatencyGapSeconds is >= 0 and <= 3_600
        && LatencyBudgetSeconds is >= 5 and <= 120;
}
