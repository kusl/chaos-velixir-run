namespace Chaos.Web.Postcards;

internal interface IPostcardSubscriber
{
    bool Deliver(PostcardUpdate update);
}
