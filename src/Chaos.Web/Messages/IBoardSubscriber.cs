namespace Chaos.Web.Messages;

internal interface IBoardSubscriber
{
    bool Deliver(BoardUpdate update);
}
