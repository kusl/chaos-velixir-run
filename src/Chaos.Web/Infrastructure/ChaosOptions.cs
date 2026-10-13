using Chaos.Web.Drawing;

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

    public int CanvasMaxPoints { get; set; } = 20_000;

    public double StrokePointBurst { get; set; } = 4_000;

    public double StrokePointsPerSecond { get; set; } = 1_500;

    public double ResetBurst { get; set; } = 2;

    public double ResetsPerSecond { get; set; } = 0.2;

    public int SlotCapacity { get; set; } = 50;

    public double PressBurst { get; set; } = 3;

    public double PressesPerSecond { get; set; } = 0.5;

    public int SpinMilliseconds { get; set; } = 4000;

    public bool IsValid() =>
        MaxConnectionsPerClient > 0
        && MaxConnections >= MaxConnectionsPerClient
        && RequestsPerSecond > 0
        && RequestBurst > 0
        && BoardCapacity > 0
        && PostBurst >= 1
        && PostsPerSecond > 0
        && CanvasMaxPoints >= CanvasService.MaxStrokePoints
        && StrokePointBurst >= CanvasService.MaxBatchPoints
        && StrokePointsPerSecond > 0
        && ResetBurst >= 1
        && ResetsPerSecond > 0
        && SlotCapacity > 0
        && PressBurst >= 1
        && PressesPerSecond > 0
        && SpinMilliseconds is >= 1000 and <= 15000;
}
