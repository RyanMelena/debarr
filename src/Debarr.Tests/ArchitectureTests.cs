using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Debarr.Detecting;
using Debarr.Playing;
using Fisher;
using Fisher.Projections;
using FluentResults;
using JasperFx.Events.Documents;
using JasperFx.Events.Projections;
using Microsoft.Extensions.DependencyInjection;

namespace Debarr.Tests;

public sealed partial class ArchitectureTests
{
    private static readonly Assembly[] Assemblies = [typeof(FileHash).Assembly, typeof(ArchitectureTests).Assembly];

    private static readonly string SourceDirectory = FindSourceDirectory();

    public static TheoryData<Type> Handlers => new(typeof(FileHash).Assembly.GetTypes()
        .Where(type => type is { IsAbstract: true, IsSealed: true } && type.GetMethod("Handle", BindingFlags.Public | BindingFlags.Static) is not null));

    [Fact]
    public void Components_keep_their_code_in_code_behind_files()
    {
        var withCodeBlocks = RazorFiles().Where(file => CodeBlock().IsMatch(File.ReadAllText(file))).Select(RelativePath);

        Assert.Empty(withCodeBlocks);
    }

    [Fact]
    public void No_namespace_has_a_segment_named_for_a_type()
    {
        var types = Assemblies.SelectMany(assembly => assembly.GetTypes()).ToList();
        var typeNames = types.Where(type => !type.IsNested).Select(type => type.Name).ToHashSet();

        var clashes = types.Select(type => type.Namespace).OfType<string>().Distinct()
            .Select(name => name.Split('.'))
            .Where(segments => segments[0] == "Debarr" && segments.Any(typeNames.Contains))
            .Select(segments => string.Join('.', segments));

        Assert.Empty(clashes);
    }

    [Theory]
    [MemberData(nameof(Handlers))]
    public void A_handler_decides_in_pure_static_functions_that_take_its_command_first(Type handler)
    {
        var handle = handler.GetMethod("Handle", BindingFlags.Public | BindingFlags.Static)!;
        var parameters = handle.GetParameters();
        Assert.Equal($"{handler.Namespace}.{handler.Name[..^"Handler".Length]}", parameters.FirstOrDefault()?.ParameterType.FullName);
        Assert.NotEqual(typeof(void), handle.ReturnType);
        Assert.DoesNotContain(parameters, parameter => parameter.ParameterType.IsAssignableTo(typeof(IQuerySession)));
        if (handler.GetMethod("Validate", BindingFlags.Public | BindingFlags.Static) is { } validate)
        {
            Assert.True(validate.ReturnType.IsAssignableTo(typeof(ResultBase)), $"{handler.Name}.Validate returns {validate.ReturnType.Name}.");
        }
    }

    [Fact]
    public async Task Every_projection_in_the_store_is_inline()
    {
        var probeOptions = new StoreOptions();
        probeOptions.Projections.Add(new ProbeProjection(), ProjectionLifecycle.Async);
        Assert.Equal([ProbeProjection.ReadModel], NotInline(probeOptions.Projections.All));

        Assert.Empty(NotInline(await StoreProjectionsAsync()));
    }

    [Fact]
    public async Task No_projection_folds_with_a_session()
    {
        var probeOptions = new StoreOptions();
        probeOptions.Projections.Add(new ProbeProjection(), ProjectionLifecycle.Inline);
        Assert.Equal(["ProbeProjection.Apply", "ProbeProjection.Create"], FoldsTakingASession(probeOptions.Projections.All));

        Assert.Empty(FoldsTakingASession(await StoreProjectionsAsync()));
    }

    [Fact]
    public void Every_text_field_and_select_is_outlined_and_dense()
    {
        var fields = RazorFiles()
            .SelectMany(file => MudFieldOpeningTag().Matches(File.ReadAllText(file)).Select(field => (File: RelativePath(file), Tag: field.Value)))
            .ToList();

        Assert.True(fields.Count > 20, $"Found only {fields.Count} fields.");
        Assert.Empty(fields.Where(field => !field.Tag.Contains("Variant=\"Variant.Outlined\"") || !field.Tag.Contains("Margin=\"Margin.Dense\"")));
    }

    [Fact]
    public void Help_of_one_sentence_is_a_fragment_and_help_of_more_ends_each_sentence_with_a_period()
    {
        var helps = RazorFiles()
            .SelectMany(file => HelpAttribute().Matches(File.ReadAllText(file)).Select(help => (File: RelativePath(file), Text: help.Groups["text"].Value)))
            .Where(help => !help.Text.StartsWith('@'))
            .ToList();

        Assert.True(helps.Count > 20, $"Found only {helps.Count} help texts.");
        Assert.Empty(helps.Where(help => help.Text.Contains(". ") != help.Text.EndsWith('.')).Select(help => $"{help.File}: {help.Text}"));
    }

    [Fact]
    public void Every_file_holds_the_one_type_it_is_named_for_or_is_a_chapter_file()
    {
        var files = SourceFiles().Select(file => (File: RelativePath(file), Name: Path.GetFileName(file).Split('.')[0], Types: TopLevelTypeNames(file))).ToList();

        Assert.True(files.Count > 200, $"Found only {files.Count} source files.");
        Assert.Empty(files.Where(file => !IsAllowed(file.File, file.Name, file.Types)).Select(file => $"{file.File}: {string.Join(", ", file.Types)}"));
    }

    private static async Task<List<IProjectionSource<IDocumentSession, IQuerySession>>> StoreProjectionsAsync()
    {
        await using var factory = new DebarrWebApplicationFactory();
        return factory.Services.GetRequiredService<IDocumentStore>().Options.Projections.All;
    }

    private static IEnumerable<string> NotInline(IEnumerable<IProjectionSource<IDocumentSession, IQuerySession>> projections) =>
        projections.Where(projection => projection.Lifecycle != ProjectionLifecycle.Inline).Select(projection => projection.Name);

    private static IEnumerable<string> FoldsTakingASession(IEnumerable<IProjectionSource<IDocumentSession, IQuerySession>> projections) =>
        projections.SelectMany(projection => projection.PublishedTypes().Prepend(projection.ImplementationType))
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                .Where(method => method.Name is "Create" or "Apply"
                    && method.GetParameters().Any(parameter => IsSession(parameter.ParameterType)))
                .Select(method => $"{type.Name}.{method.Name}"))
            .Distinct()
            .Order(StringComparer.Ordinal);

    private static bool IsSession(Type type) => type.IsAssignableTo(typeof(IDocumentReadOperations));

    private static bool IsAllowed(string file, string name, IReadOnlyList<string> types) =>
        name == "Events"
        || types.SequenceEqual([name])
        || (types.Contains(name) && (
            types.All(type => type == name || type == name + "Handler")
            || (types.Contains(name + "Projection") && types.All(type => type.StartsWith(name, StringComparison.Ordinal)))
            || IsAggregate(file, name)));

    private static bool IsAggregate(string file, string name) =>
        Assemblies.Select(assembly => assembly.GetType($"{NamespaceOf(file)}.{name}")).OfType<Type>().Any(type =>
            type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Any(method => method.Name is "Create" or "Apply" && method.GetParameters().Length > 0));

    private static string NamespaceOf(string file) => string.Join('.', Path.GetDirectoryName(file)!.Split(Path.DirectorySeparatorChar));

    private static List<string> TopLevelTypeNames(string file) =>
        [.. TypeDeclaredAtLineStart().Matches(File.ReadAllText(file)).Select(match => match.Groups["name"].Value)];

    private static IEnumerable<string> RazorFiles() => Directory.EnumerateFiles(Path.Combine(SourceDirectory, "Debarr", "Components"), "*.razor", SearchOption.AllDirectories);

    private static IEnumerable<string> SourceFiles() =>
        new[] { "Debarr", "Debarr.Tests" }
            .SelectMany(project => Directory.EnumerateFiles(Path.Combine(SourceDirectory, project), "*.cs", SearchOption.AllDirectories))
            .Where(file => RelativePath(file).Split(Path.DirectorySeparatorChar) is var segments
                && !segments.Contains("bin") && !segments.Contains("obj") && !segments.Contains("Generated"));

    private static string RelativePath(string file) => Path.GetRelativePath(SourceDirectory, file);

    private static string FindSourceDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(Path.GetDirectoryName(path))!;

    [GeneratedRegex(@"^\s*@(code|functions)\b", RegexOptions.Multiline)]
    private static partial Regex CodeBlock();

    [GeneratedRegex(@"\b(HelperText|Help)=""(?<text>[^""]*)""")]
    private static partial Regex HelpAttribute();

    [GeneratedRegex(@"<(Mud\w*Field|MudSelect)\b(""[^""]*""|[^"">])*>")]
    private static partial Regex MudFieldOpeningTag();

    [GeneratedRegex(@"^(\[.*\]\s*)?((public|internal|file|sealed|static|abstract|partial|readonly|ref)\s+)*(record\s+(class|struct)|class|record|struct|interface|enum|delegate\s+[\w<>\[\],.? ]+?)\s+(?<name>\w+)", RegexOptions.Multiline)]
    private static partial Regex TypeDeclaredAtLineStart();

    private sealed partial class ProbeProjection : SingleStreamProjection<HistoryClear, Guid>
    {
        public const string ReadModel = "Probe";

        public ProbeProjection() => Name = ReadModel;

        public static HistoryClear Create(HistoryCleared cleared, IQuerySession session) => new() { ClearedAt = cleared.ClearedAt };

        public static void Apply(HistoryCleared cleared, HistoryClear clear, IQuerySession session) => clear.ClearedAt = cleared.ClearedAt;
    }
}
