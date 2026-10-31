namespace Chaos.Web.Postcards;

internal sealed record PostcardReply(PostcardResult Result, Postcard? Card = null);
