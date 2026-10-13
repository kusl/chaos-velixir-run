namespace Chaos.Web.Drawing;

internal sealed class Stroke(long id, int epoch, int clientId)
{
    private readonly List<int> _points = [];

    public long Id { get; } = id;

    public int Epoch { get; } = epoch;

    public int ClientId { get; } = clientId;

    public bool Done { get; set; }

    public int Count => _points.Count / 2;

    public void Append(IReadOnlyList<int> points) => _points.AddRange(points);

    public int[] ToArray() => [.. _points];
}
