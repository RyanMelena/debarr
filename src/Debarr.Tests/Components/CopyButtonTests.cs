using Bunit;
using Debarr.Components;
using Debarr.Tests.Extensions;
using MudBlazor.Services;

namespace Debarr.Tests.Components;

public sealed class CopyButtonTests : BunitContext
{
    public CopyButtonTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public async Task A_click_copies_the_value_and_says_so()
    {
        var module = JSInterop.SetupModule("./Components/CopyButton.razor.js");
        module.SetupVoid("copy", @"\\nas\media\Heat.mkv").SetVoidResult();
        var cut = Render<CopyButton>(parameters => parameters.Add(button => button.Value, @"\\nas\media\Heat.mkv"));
        Assert.Equal("Copy Path", cut.Find("button").GetAttribute("aria-label"));

        await cut.RaiseClickAsync("button", TimeSpan.FromSeconds(10));

        cut.WaitForAssertion(() => Assert.Equal("Copied", cut.Find("button").GetAttribute("aria-label")));
        Assert.Single(module.VerifyInvoke("copy").Arguments, @"\\nas\media\Heat.mkv");
    }
}
