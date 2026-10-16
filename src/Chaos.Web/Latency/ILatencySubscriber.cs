namespace Chaos.Web.Latency;

internal interface ILatencySubscriber
{
    bool Deliver(LatencyUpdate update);
}
