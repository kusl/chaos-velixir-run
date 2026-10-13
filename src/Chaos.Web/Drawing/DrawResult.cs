namespace Chaos.Web.Drawing;

internal enum DrawResult
{
    Accepted = 0,
    Stale = 1,
    TooFast = 2,
    Invalid = 3,
    Full = 4,
    TooLong = 5,
}
