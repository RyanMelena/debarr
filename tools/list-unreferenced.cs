#!/usr/bin/env dotnet
// Lists the types and members declared in a project that no code in that project references, as tab-separated rows, for the cleanup review.
//   dotnet run tools/list-unreferenced.cs                     each Debarr symbol: kind, symbol, file:line and how many references Debarr.Tests makes to it
//   dotnet run tools/list-unreferenced.cs -- Debarr.Tests     each Debarr.Tests symbol, with the test classes left out
// Paths are relative to src/. Generated files, components, extension and query classes, overrides, interface implementations,
// and the members Wolverine, Fisher, Blazor, xUnit and System.Text.Json reach by convention are left out, since no code names them.
#:package Microsoft.CodeAnalysis.Workspaces.MSBuild@5.9.0
#:package Microsoft.CodeAnalysis.CSharp.Workspaces@5.9.0
#:property PublishAot=false

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.MSBuild;

var source = Path.GetFullPath(Path.Combine(AppContext.GetData("EntryPointFileDirectoryPath") as string ?? ".", "..", "src"));
string[] conventionMethods = ["Apply", "Create", "Handle", "HandleAsync", "Validate", "LoadAsync", "ShouldDelete", "Before", "After", "Evolve", "Configure", "Execute", "BuildRenderTree", "Main", "<Main>$"];
string[] conventionAttributes = ["ParameterAttribute", "CascadingParameterAttribute", "InjectAttribute", "SupplyParameterFromQueryAttribute", "SupplyParameterFromFormAttribute", "JSInvokableAttribute", "IdentityAttribute", "TheoryAttribute", "FactAttribute"];

using var workspace = MSBuildWorkspace.Create();
var solution = await workspace.OpenSolutionAsync(Path.Combine(source, "Debarr.sln"));
var projectName = args.FirstOrDefault() ?? "Debarr";
var project = solution.Projects.Single(project => project.Name == projectName);
var compilation = await project.GetCompilationAsync() ?? throw new InvalidOperationException(projectName);

var rows = new List<(string Kind, string Symbol, string Location, int TestReferences)>();

foreach (var type in AllTypes(compilation.Assembly.GlobalNamespace))
{
    if (!IsOwnSource(type))
    {
        continue;
    }

    var isContainer = type.IsStatic && (type.Name.EndsWith("Extensions", StringComparison.Ordinal) || type.Name.EndsWith("Query", StringComparison.Ordinal));
    if (!isContainer && !type.Name.EndsWith("Tests", StringComparison.Ordinal) && !InheritsFrom(type, "Microsoft.AspNetCore.Components.ComponentBase"))
    {
        await Check(type, "type");
    }

    foreach (var member in type.GetMembers())
    {
        if (member.IsImplicitlyDeclared || !IsOwnSource(member) || IsReachedByConvention(member))
        {
            continue;
        }

        await Check(member, member.Kind.ToString().ToLowerInvariant());
    }
}

Console.WriteLine($"kind\tsymbol\tlocation\treferences outside {projectName}");
foreach (var row in rows.OrderBy(row => row.Location, StringComparer.Ordinal))
{
    Console.WriteLine($"{row.Kind}\t{row.Symbol}\t{row.Location}\t{row.TestReferences}");
}

return 0;

async Task Check(ISymbol symbol, string kind)
{
    var ownReferences = 0;
    var otherReferences = 0;
    foreach (var found in await SymbolFinder.FindReferencesAsync(symbol, solution))
    {
        foreach (var location in found.Locations)
        {
            if (location.Document.Project.Name != projectName)
            {
                otherReferences++;
            }
            else if (!IsInside(location.Location, symbol))
            {
                ownReferences++;
            }
        }
    }

    if (ownReferences == 0)
    {
        var span = symbol.Locations[0].GetLineSpan();
        rows.Add((kind, symbol.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat), $"{Path.GetRelativePath(source, span.Path).Replace('\\', '/')}:{span.StartLinePosition.Line + 1}", otherReferences));
    }
}

bool IsOwnSource(ISymbol symbol) =>
    symbol.Locations.FirstOrDefault(location => location.IsInSource) is { } location
    && location.SourceTree!.FilePath.StartsWith(source, StringComparison.OrdinalIgnoreCase)
    && !location.SourceTree.FilePath.Contains("Generated", StringComparison.Ordinal)
    && !location.SourceTree.FilePath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

bool IsReachedByConvention(ISymbol member) =>
    member.IsOverride
    || member.GetAttributes().Any(attribute => conventionAttributes.Contains(attribute.AttributeClass?.Name))
    || member is IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor or MethodKind.PropertyGet or MethodKind.PropertySet or MethodKind.EventAdd or MethodKind.EventRemove or MethodKind.UserDefinedOperator or MethodKind.Conversion }
    || member is IMethodSymbol method && (conventionMethods.Contains(method.Name) || method.IsPartialDefinition || method.PartialImplementationPart is not null)
    || member is IPropertySymbol { DeclaredAccessibility: Accessibility.Public, ContainingType.IsRecord: true }
    || member.ContainingType.AllInterfaces.SelectMany(face => face.GetMembers()).Any(faceMember => SymbolEqualityComparer.Default.Equals(member.ContainingType.FindImplementationForInterfaceMember(faceMember), member));

static bool InheritsFrom(INamedTypeSymbol type, string name)
{
    for (var current = type; current is not null; current = current.BaseType)
    {
        if (current.ToDisplayString() == name)
        {
            return true;
        }
    }

    return false;
}

static bool IsInside(Location reference, ISymbol symbol) =>
    symbol.DeclaringSyntaxReferences.Any(declaration => declaration.SyntaxTree == reference.SourceTree && declaration.Span.Contains(reference.SourceSpan));

static IEnumerable<INamedTypeSymbol> AllTypes(INamespaceSymbol space)
{
    foreach (var member in space.GetMembers())
    {
        if (member is INamespaceSymbol child)
        {
            foreach (var type in AllTypes(child))
            {
                yield return type;
            }
        }
        else if (member is INamedTypeSymbol type)
        {
            foreach (var nested in Nested(type))
            {
                yield return nested;
            }
        }
    }
}

static IEnumerable<INamedTypeSymbol> Nested(INamedTypeSymbol type)
{
    yield return type;
    foreach (var nested in type.GetTypeMembers().SelectMany(Nested))
    {
        yield return nested;
    }
}
