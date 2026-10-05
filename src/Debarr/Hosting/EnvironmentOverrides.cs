using System.Collections;

namespace Debarr.Hosting;

/// <summary>Matches a configuration key to the environment variable that overrides it.</summary>
public static class EnvironmentOverrides
{
    public const string Prefix = "DEBARR__";

    /// <summary>The environment variable that supplies this configuration key, or null when none does.</summary>
    public static string? FindVariable(string key) => FindVariable(key, Environment.GetEnvironmentVariables());

    public static string? FindVariable(string key, IDictionary variables)
    {
        var name = Prefix + string.Join("__", key.Split(':'));

        foreach (DictionaryEntry entry in variables)
        {
            // The name is returned as the environment spells it, so the operator sees their own variable.
            if (entry.Key is string existing && string.Equals(existing, name, StringComparison.OrdinalIgnoreCase))
            {
                return existing;
            }
        }

        return null;
    }
}
