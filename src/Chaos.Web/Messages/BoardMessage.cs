using System.Text.Json.Serialization;

namespace Chaos.Web.Messages;

internal sealed record BoardMessage(
    Guid Id,
    string Text,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<MetaEntry>? Meta = null);
