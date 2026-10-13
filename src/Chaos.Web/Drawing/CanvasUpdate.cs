using System.Text.Json.Serialization;

namespace Chaos.Web.Drawing;

internal sealed record StrokeSnapshot(long Id, IReadOnlyList<int> Points, bool Done);

internal sealed record CanvasUpdate(
    string Kind,
    int Epoch,
    long Stroke,
    int Offset,
    IReadOnlyList<int> Points,
    bool Done,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<StrokeSnapshot>? Strokes)
{
    public static CanvasUpdate ForStroke(int epoch, long stroke, int offset, IReadOnlyList<int> points, bool done) =>
        new("stroke", epoch, stroke, offset, points, done, null);

    public static CanvasUpdate ForReset(int epoch) => new("reset", epoch, 0, 0, [], true, null);

    public static CanvasUpdate ForSnapshot(int epoch, IReadOnlyList<StrokeSnapshot> strokes) =>
        new("snapshot", epoch, 0, 0, [], true, strokes);
}
