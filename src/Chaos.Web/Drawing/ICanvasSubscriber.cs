namespace Chaos.Web.Drawing;

internal interface ICanvasSubscriber
{
    bool Deliver(CanvasUpdate update);
}
