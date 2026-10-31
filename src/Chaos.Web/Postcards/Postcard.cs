using System.Text.Json.Serialization;
using Chaos.Web.Messages;

namespace Chaos.Web.Postcards;

internal sealed record Postcard(
    Guid Id,
    string Text,
    IReadOnlyList<PostcardImage> Images,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<MetaEntry>? Meta = null);
