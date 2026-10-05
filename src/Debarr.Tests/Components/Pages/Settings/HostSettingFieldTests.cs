using Bunit;
using Debarr.Components.Pages.Settings;
using MudBlazor.Services;

namespace Debarr.Tests.Components.Pages.Settings;

public sealed class HostSettingFieldTests : BunitContext
{
    public HostSettingFieldTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void A_setting_an_environment_variable_sets_is_locked_and_its_help_names_the_variable()
    {
        var key = $"Test:Setting{Guid.NewGuid():N}";
        var variable = "DEBARR__" + key.Replace(":", "__", StringComparison.Ordinal);
        Environment.SetEnvironmentVariable(variable, "set");
        try
        {
            var cut = Render<HostSettingField<string>>(field => field
                .Add(parameter => parameter.Key, key)
                .Add(parameter => parameter.Label, "Setting")
                .Add(parameter => parameter.Help, "What it does.")
                .Add(parameter => parameter.Value, "set"));

            Assert.True(cut.Find("input").HasAttribute("readonly"));
            Assert.NotNull(cut.Find(".set-by-variable"));
            Assert.Contains($"Set by {variable}. What it does.", cut.Markup);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void A_setting_no_environment_variable_sets_is_edited_and_shows_its_help()
    {
        var cut = Render<HostSettingField<string>>(field => field
            .Add(parameter => parameter.Key, $"Test:Setting{Guid.NewGuid():N}")
            .Add(parameter => parameter.Label, "Setting")
            .Add(parameter => parameter.Help, "What it does.")
            .Add(parameter => parameter.Value, ""));

        Assert.False(cut.Find("input").HasAttribute("readonly"));
        Assert.Empty(cut.FindAll(".set-by-variable"));
        Assert.Contains("What it does.", cut.Markup);
        Assert.DoesNotContain("Set by", cut.Markup);
    }
}
