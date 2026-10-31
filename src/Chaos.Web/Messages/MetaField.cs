namespace Chaos.Web.Messages;

internal sealed record MetaField(string Key, string Label, int MaxLength = MessageMeta.MaxValueLength, double? Min = null, double? Max = null)
{
    public bool IsNumber => Min is not null || Max is not null;
}
