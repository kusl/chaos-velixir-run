using System.Text.Json.Serialization;

namespace Chaos.Web.Drawing;

internal sealed record StrokeSnapshot(Guid Id, IReadOnlyList<int> Points, bool Done);

internal sealed record CanvasUpdate(
    string Kind,
    int Epoch,
    Guid Stroke,
    int Offset,
    IReadOnlyList<int> Points,
    bool Done,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<StrokeSnapshot>? Strokes,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? Reset,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? Started,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Room)
{
    public static CanvasUpdate ForStroke(int epoch, Guid stroke, int offset, IReadOnlyList<int> points, bool done) =>
        new("stroke", epoch, stroke, offset, points, done, null, null, null, null);

    public static CanvasUpdate ForReset(int epoch, Guid reset) => new("reset", epoch, Guid.Empty, 0, [], true, null, reset, null, null);

    public static CanvasUpdate ForMerge(int epoch, IReadOnlyList<StrokeSnapshot> strokes) =>
        new("merge", epoch, Guid.Empty, 0, [], true, strokes, null, null, null);

    public static CanvasUpdate ForSnapshot(int epoch, IReadOnlyList<StrokeSnapshot> strokes, Guid? reset, long started, int room) =>
        new("snapshot", epoch, Guid.Empty, 0, [], true, strokes, reset, started, room);
}
