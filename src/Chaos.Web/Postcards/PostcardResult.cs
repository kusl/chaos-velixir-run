namespace Chaos.Web.Postcards;

internal enum PostcardResult
{
    Accepted,
    Empty,
    TooFast,
    Repeat,
    TooManyImages,
    TooLarge,
    BadImage,
    Busy,
    Failed,
}
