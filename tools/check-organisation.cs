#!/usr/bin/env dotnet
// Checks Debarr and Debarr.Tests against the type, file and folder rules, for the organisation review.
//   dotnet run tools/check-organisation.cs            one tab-separated row per finding: rule, location, detail; exit code 1 when any rule fails
//   dotnet run tools/check-organisation.cs -- all     also lists public types with the reason each is public, nested types, and test helpers, for judgement
// Paths are relative to src/. Generated files are skipped.
#:package Microsoft.CodeAnalysis.Workspaces.MSBuild@5.9.0
#:package Microsoft.CodeAnalysis.CSharp.Workspaces@5.9.0
#:property PublishAot=false

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;

var listAll = args.FirstOrDefault() == "all";
var source = Path.GetFullPath(Path.Combine(AppContext.GetData("EntryPointFileDirectoryPath") as string ?? ".", "..", "src"));

using var workspace = MSBuildWorkspace.Create();
var solution = await workspace.OpenSolutionAsync(Path.Combine(source, "Debarr.sln"));

var findings = new List<(string Rule, string Location, string Detail)>();
var notes = new List<(string Rule, string Location, string Detail)>();
var appTypes = new Dictionary<string, (INamedTypeSymbol Type, string Folder)>(StringComparer.Ordinal);
var testFiles = new List<(string Path, string Folder, string TypeName)>();
var bodies = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
var generatedText = new List<string>();
var testTypes = new List<INamedTypeSymbol>();
Compilation? appCompilation = null;

foreach (var project in solution.Projects)
{
    var compilation = await project.GetCompilationAsync() ?? throw new InvalidOperationException(project.Name);
    var isTests = project.Name.EndsWith(".Tests", StringComparison.Ordinal);
    var projectFolder = Path.Combine(source, project.Name);
    if (!isTests)
    {
        appCompilation = compilation;
    }

    foreach (var tree in compilation.SyntaxTrees)
    {
        if (!isTests && tree.FilePath.Contains($"{Path.DirectorySeparatorChar}Internal{Path.DirectorySeparatorChar}Generated{Path.DirectorySeparatorChar}"))
        {
            generatedText.Add((await tree.GetTextAsync()).ToString());
        }

        if (!tree.FilePath.StartsWith(projectFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || IsSkipped(tree.FilePath))
        {
            continue;
        }

        var relative = Relative(tree.FilePath);
        var folder = Path.GetDirectoryName(Path.GetRelativePath(projectFolder, tree.FilePath))!.Replace('\\', '/');
        var expectedNamespace = folder.Length == 0 ? project.Name : $"{project.Name}.{folder.Replace('/', '.')}";
        var fileTypeName = Path.GetFileName(tree.FilePath).Split('.')[0];
        var model = compilation.GetSemanticModel(tree);
        var root = await tree.GetRootAsync();

        var topLevel = root.DescendantNodes(node => node is not BaseTypeDeclarationSyntax and not DelegateDeclarationSyntax)
            .Where(node => node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax)
            .Select(node => (INamedTypeSymbol)model.GetDeclaredSymbol(node)!)
            .ToList();

        if (topLevel.Count == 0 && root.DescendantNodes().OfType<GlobalStatementSyntax>().Any())
        {
            appTypes.TryAdd("Program", (compilation.GetTypeByMetadataName("Program")!, folder));
        }
        else if (topLevel.Count != 1 && !IsChapterFile(tree.FilePath, fileTypeName, topLevel))
        {
            findings.Add(("one-type-per-file", relative, $"{topLevel.Count} top-level types: {string.Join(", ", topLevel.Select(type => type.Name))}"));
        }

        foreach (var type in topLevel)
        {
            if (type.Name != fileTypeName && !(type.IsGenericType && type.Name == fileTypeName) && !IsChapterFile(tree.FilePath, fileTypeName, topLevel))
            {
                findings.Add(("file-name", relative, $"{type.Name} in {Path.GetFileName(tree.FilePath)}"));
            }

            var space = type.ContainingNamespace.ToDisplayString();
            if (space != expectedNamespace && !(type.ContainingNamespace.IsGlobalNamespace && type.Name == "Program"))
            {
                findings.Add(("namespace", relative, $"{type.Name} is in {space}, folder says {expectedNamespace}"));
            }

            if (isTests)
            {
                testFiles.Add((relative, folder, type.Name));
                testTypes.Add(type);
            }
            else
            {
                appTypes.TryAdd(type.Name, (type, folder));
            }

            foreach (var nested in type.GetTypeMembers())
            {
                notes.Add(("nested", relative, $"{type.Name}.{nested.Name} ({nested.DeclaredAccessibility})"));
            }
        }

        foreach (var body in root.DescendantNodes().Where(node => node is BlockSyntax { Parent: BaseMethodDeclarationSyntax or AccessorDeclarationSyntax or LocalFunctionStatementSyntax } or ArrowExpressionClauseSyntax))
        {
            if (body.DescendantTokens().Count() >= 25)
            {
                var member = body.Ancestors().OfType<MemberDeclarationSyntax>().First();
                var name = member switch
                {
                    MethodDeclarationSyntax method => method.Identifier.Text,
                    PropertyDeclarationSyntax property => property.Identifier.Text,
                    ConstructorDeclarationSyntax => ".ctor",
                    _ => member.Kind().ToString(),
                };
                Add(bodies, body.WithoutTrivia().NormalizeWhitespace().ToFullString(), $"{relative}:{tree.GetLineSpan(body.Span).StartLinePosition.Line + 1} {name}");
            }
        }

        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>().Select(node => model.GetDeclaredSymbol(node)).OfType<IMethodSymbol>().Where(method => method.IsExtensionMethod))
        {
            var extended = method.Parameters[0].Type;
            var extendedName = extended is ITypeParameterSymbol parameter
                ? parameter.ConstraintTypes.FirstOrDefault()?.Name ?? "Object"
                : extended.Name;
            if (method.ContainingType.Name == $"{fileTypeName}Query" && IsChapterFile(tree.FilePath, fileTypeName, topLevel))
            {
                continue;
            }

            var extendedFolder = appTypes.TryGetValue(extendedName, out var own) ? own.Folder : IsDebarr(extended) ? null : "Extensions";
            if (extendedFolder is not null && folder != extendedFolder)
            {
                findings.Add(("extension-folder", relative, $"{method.ContainingType.Name}.{method.Name} extends {extendedName} outside {extendedFolder}/"));
            }

            if (method.ContainingType.Name != $"{extendedName}Extensions")
            {
                findings.Add(("extension-class", relative, $"{method.ContainingType.Name}.{method.Name} extends {extendedName}"));
            }
        }
    }

    foreach (var markup in project.AdditionalDocuments.Where(document => document.FilePath?.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) == true))
    {
        var text = (await markup.GetTextAsync()).ToString();
        var relative = Relative(markup.FilePath!);
        if (System.Text.RegularExpressions.Regex.IsMatch(text, @"@(code|functions)\s*\{"))
        {
            findings.Add(("code-behind", relative, "markup holds an @code block"));
        }

        if (!File.Exists(markup.FilePath + ".cs") && text.Contains("@inject", StringComparison.Ordinal))
        {
            findings.Add(("code-behind", relative, "injects a service but has no code-behind"));
        }
    }
}

if (appCompilation is null)
{
    throw new InvalidOperationException("Debarr project not found");
}

foreach (var (name, (type, _)) in appTypes)
{
    var location = Relative(type.Locations[0].SourceTree!.FilePath);
    if (type.DeclaredAccessibility == Accessibility.Public)
    {
        var reason = PublicReason(type);
        if (reason is null)
        {
            findings.Add(("internal", location, $"{name} is public and no component or public signature needs it"));
        }
        else
        {
            notes.Add(("public", location, $"{name}: {reason}"));
        }
    }
}

foreach (var (path, folder, typeName) in testFiles)
{
    if (!typeName.EndsWith("Tests", StringComparison.Ordinal))
    {
        notes.Add(("test-helper", path, typeName));
        continue;
    }

    // XTests or X<Aspect>Tests tests the Debarr type X, preferring the longest match.
    var tested = appTypes.Keys.Where(name => typeName.StartsWith(name, StringComparison.Ordinal)).MaxBy(name => name.Length);
    if (tested is null && !Directory.Exists(Path.Combine(source, "Debarr", folder)))
    {
        findings.Add(("test-mirror", path, $"{typeName} names no Debarr type"));
    }
    else if (tested is not null && appTypes[tested].Folder is var targetFolder && targetFolder != folder)
    {
        findings.Add(("test-mirror", path, $"{tested} is in {(targetFolder.Length == 0 ? "the project root" : targetFolder + "/")}"));
    }
}

foreach (var locations in bodies.Values.Where(locations => locations.Count > 1))
{
    notes.Add(("duplicate-body", locations.First(), string.Join(", ", locations.Skip(1))));
}

Console.WriteLine("rule\tlocation\tdetail");
foreach (var row in findings.Concat(listAll ? notes : []).OrderBy(row => row.Rule, StringComparer.Ordinal).ThenBy(row => row.Location, StringComparer.Ordinal))
{
    Console.WriteLine($"{row.Rule}\t{row.Location}\t{row.Detail}");
}

return findings.Count == 0 ? 0 : 1;

// A public type needs to be public when it is a component, a Fisher projection, named by Wolverine's generated handlers,
// or exposed by a public or protected member of another public type, a test's included.
string? PublicReason(INamedTypeSymbol type)
{
    if (InheritsFrom(type, "Microsoft.AspNetCore.Components.ComponentBase"))
    {
        return "component";
    }

    if (type.ContainingNamespace.IsGlobalNamespace && type.Name == "Program")
    {
        return "the web SDK makes the entry point public for WebApplicationFactory";
    }

    if (InheritsFrom(type, "JasperFx.Events.Projections.ProjectionBase") || type.AllInterfaces.Any(face => face.ContainingNamespace.ToDisplayString().StartsWith("JasperFx.Events.Projections", StringComparison.Ordinal)))
    {
        return "Fisher projection";
    }

    if (generatedText.Any(text => System.Text.RegularExpressions.Regex.IsMatch(text, $@"\b{type.Name}\b")))
    {
        return "Wolverine's generated handlers name it";
    }

    foreach (var other in appTypes.Values.Select(entry => entry.Type).Concat(testTypes).SelectMany(Nested))
    {
        if (other.DeclaredAccessibility != Accessibility.Public || SymbolEqualityComparer.Default.Equals(other, type))
        {
            continue;
        }

        if (other.BaseType is { } baseType && Unwrap(baseType).Any(baseOrArgument => baseOrArgument.ToDisplayString() == type.ToDisplayString()))
        {
            return $"base of {other.Name}";
        }

        foreach (var member in other.GetMembers().Where(member => member.DeclaredAccessibility is Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal && !member.IsImplicitlyDeclared))
        {
            var exposed = member switch
            {
                IPropertySymbol property => Unwrap(property.Type),
                IFieldSymbol field => Unwrap(field.Type),
                IMethodSymbol method => Unwrap(method.ReturnType).Concat(method.Parameters.SelectMany(parameter => Unwrap(parameter.Type))),
                _ => [],
            };
            if (exposed.Any(exposedType => exposedType.ToDisplayString() == type.ToDisplayString()))
            {
                return $"{other.Name}.{member.Name}";
            }
        }
    }

    return null;
}

static IEnumerable<INamedTypeSymbol> Nested(INamedTypeSymbol type) => type.GetTypeMembers().SelectMany(Nested).Prepend(type);

static bool IsDebarr(ITypeSymbol type) => type.ContainingNamespace?.ToDisplayString().StartsWith("Debarr", StringComparison.Ordinal) == true;

// A chapter is a folder with an Events.cs; its aggregate, events, slice and read model files hold several types, one of them named for the file.
static bool IsChapterFile(string path, string fileTypeName, List<INamedTypeSymbol> topLevel) =>
    File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "Events.cs"))
    && (fileTypeName == "Events" || topLevel.Any(type => type.Name == fileTypeName));

string Relative(string path) => Path.GetRelativePath(source, path).Replace('\\', '/');

static bool IsSkipped(string path) =>
    path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
    || path.Contains($"{Path.DirectorySeparatorChar}Internal{Path.DirectorySeparatorChar}Generated{Path.DirectorySeparatorChar}");

// The named types a type is built from: itself, its type arguments and its array elements.
static IEnumerable<INamedTypeSymbol> Unwrap(ITypeSymbol type) => type switch
{
    IArrayTypeSymbol array => Unwrap(array.ElementType),
    INamedTypeSymbol named => [named.OriginalDefinition, .. named.TypeArguments.SelectMany(Unwrap)],
    _ => [],
};

static void Add(Dictionary<string, SortedSet<string>> map, string key, string value)
{
    if (!map.TryGetValue(key, out var set))
    {
        map[key] = set = new SortedSet<string>(StringComparer.Ordinal);
    }

    set.Add(value);
}

static bool InheritsFrom(INamedTypeSymbol type, string name)
{
    for (var current = type; current is not null; current = current.BaseType)
    {
        if (current.OriginalDefinition.ToDisplayString() == name)
        {
            return true;
        }
    }

    return false;
}
