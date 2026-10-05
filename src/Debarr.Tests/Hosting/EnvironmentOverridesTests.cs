using System.Collections;
using Debarr.Hosting;

namespace Debarr.Tests.Hosting;

public class EnvironmentOverridesTests
{
    [Fact]
    public void Prefixed_variable_overrides_its_key()
    {
        var variables = new Dictionary<string, string> { ["DEBARR__SERVER__PORT"] = "8181" };

        Assert.Equal("DEBARR__SERVER__PORT", EnvironmentOverrides.FindVariable("Server:Port", (IDictionary)variables));
    }

    [Fact]
    public void A_match_returns_the_spelling_the_environment_uses()
    {
        var variables = new Dictionary<string, string> { ["debarr__server__port"] = "8181" };

        Assert.Equal("debarr__server__port", EnvironmentOverrides.FindVariable("Server:Port", (IDictionary)variables));
    }

    [Fact]
    public void A_nested_key_joins_every_segment()
    {
        var variables = new Dictionary<string, string> { ["DEBARR__LOGGING__LOGLEVEL__DEFAULT"] = "Debug" };

        Assert.Equal(
            "DEBARR__LOGGING__LOGLEVEL__DEFAULT",
            EnvironmentOverrides.FindVariable("Logging:LogLevel:Default", (IDictionary)variables));
    }

    [Fact]
    public void An_unprefixed_variable_overrides_nothing()
    {
        var variables = new Dictionary<string, string> { ["SERVER__PORT"] = "8181" };

        Assert.Null(EnvironmentOverrides.FindVariable("Server:Port", (IDictionary)variables));
    }

    [Fact]
    public void An_absent_variable_overrides_nothing()
    {
        var variables = new Dictionary<string, string>();

        Assert.Null(EnvironmentOverrides.FindVariable("Server:Port", (IDictionary)variables));
    }
}
