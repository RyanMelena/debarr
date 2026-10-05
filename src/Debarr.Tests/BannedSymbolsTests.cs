using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyModel;

namespace Debarr.Tests;

/// <summary>The banned API analyzer ignores an entry that matches nothing.</summary>
public sealed class BannedSymbolsTests
{
    private static readonly string SourceDirectory = FindSourceDirectory();

    [Theory]
    [InlineData("Debarr")]
    [InlineData("Debarr.Tests")]
    public void Every_banned_symbol_names_a_symbol_the_project_references(string project)
    {
        var compilation = CompilationReferencing(project);

        var unresolved = File.ReadLines(Path.Combine(SourceDirectory, project, "BannedSymbols.txt"))
            .Select((line, index) => (Line: index + 1, Id: line.Split(';')[0].Trim()))
            .Where(entry => entry.Id.Length > 0 && DocumentationCommentId.GetSymbolsForDeclarationId(entry.Id, compilation).IsEmpty)
            .Select(entry => $"{project}/BannedSymbols.txt:{entry.Line}: {entry.Id}")
            .ToList();

        Assert.True(unresolved.Count == 0, $"These entries name no symbol:{Environment.NewLine}{string.Join(Environment.NewLine, unresolved)}");
    }

    private static CSharpCompilation CompilationReferencing(string project)
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, $"{project}.deps.json"));
        using var reader = new DependencyContextJsonReader();
        var context = reader.Read(stream);

        var projectAssemblies = context.RuntimeLibraries
            .SelectMany(library => library.GetDefaultAssemblyNames(context))
            .Select(name => Path.Combine(AppContext.BaseDirectory, $"{name.Name}.dll"));
        var frameworkAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => !path.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase));

        return CSharpCompilation.Create(project, references: projectAssemblies.Concat(frameworkAssemblies).Select(path => MetadataReference.CreateFromFile(path)));
    }

    private static string FindSourceDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(Path.GetDirectoryName(path))!;
}
