namespace Chaos.Web.Messages;

internal sealed record PostReply(PostResult Result, BoardMessage? Message = null);
