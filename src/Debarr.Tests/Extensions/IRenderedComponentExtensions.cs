using Bunit;
using Microsoft.AspNetCore.Components;

namespace Debarr.Tests.Extensions;

/// <summary>
/// Each call waits for its element to render, then raises the event on the renderer and returns once the handler has run up to its first wait and the page has rendered.
/// What the handler finishes later shows later, so a test checks it with WaitForAssertion.
/// </summary>
public static class IRenderedComponentExtensions
{
    public static Task RaiseClickAsync<TComponent>(this IRenderedComponent<TComponent> cut, string selector, TimeSpan timeout)
        where TComponent : IComponent
    {
        var element = cut.WaitForElement(selector, timeout);
        return cut.InvokeAsync(() => { _ = element.ClickAsync(); });
    }

    public static Task RaiseInputAsync<TComponent>(this IRenderedComponent<TComponent> cut, string selector, string value, TimeSpan timeout)
        where TComponent : IComponent
    {
        var element = cut.WaitForElement(selector, timeout);
        return cut.InvokeAsync(() => { _ = element.InputAsync(value); });
    }
}
