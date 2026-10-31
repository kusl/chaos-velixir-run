namespace Chaos.Web.Postcards;

internal sealed record PostcardUpdate(string Kind, long Version, Postcard? Card)
{
    public static PostcardUpdate Snapshot(long version, Postcard? card) => new("snapshot", version, card);

    public static PostcardUpdate Change(long version, Postcard? card) => new("change", version, card);
}
