namespace Chaos.Web.Postcards;

public sealed record PostcardSent(string? Id, string? Status)
{
    internal static PostcardSent From(PostcardReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        return new(reply.Card?.Id.ToString("D"), StatusOf(reply.Result));
    }

    internal static string? StatusOf(PostcardResult result) => result switch
    {
        PostcardResult.Accepted => null,
        PostcardResult.Empty => "Write something or add an image first.",
        PostcardResult.TooFast => "Slow down. You can send another postcard in a moment.",
        PostcardResult.Repeat => "That postcard is already up.",
        PostcardResult.TooManyImages => $"Attach at most {PostcardRules.MaxImages} images.",
        PostcardResult.TooLarge => "Each image can be 2 MB and all of them 4 MB.",
        PostcardResult.BadImage => "One of the images is not a PNG, JPEG or GIF this site accepts, or it is larger than 4096 pixels on a side.",
        PostcardResult.Busy => "Other people are sending images right now. Try again in a moment.",
        _ => "Could not send. Try again.",
    };
}
