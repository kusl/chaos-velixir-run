using Microsoft.JSInterop;

namespace Chaos.Web.Infrastructure;

internal static class JsCleanup
{
    public static async ValueTask DisposeAsync(IJSObjectReference? reference, string? method)
    {
        if (reference is null)
        {
            return;
        }

        try
        {
            if (method is not null)
            {
                await reference.InvokeVoidAsync(method).ConfigureAwait(false);
            }

            await reference.DisposeAsync().ConfigureAwait(false);
        }
        catch (JSDisconnectedException)
        {
        }
        catch (JSException)
        {
        }
        catch (TaskCanceledException)
        {
        }
    }

    public static async ValueTask InvokeAsync(IJSObjectReference? reference, string method)
    {
        if (reference is null)
        {
            return;
        }

        try
        {
            await reference.InvokeVoidAsync(method).ConfigureAwait(false);
        }
        catch (JSDisconnectedException)
        {
        }
        catch (JSException)
        {
        }
        catch (TaskCanceledException)
        {
        }
    }
}
