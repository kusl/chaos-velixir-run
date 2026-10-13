namespace Chaos.Web.Slots;

internal interface ISlotSubscriber
{
    bool Deliver(SlotUpdate update);
}
