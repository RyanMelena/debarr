#!/usr/bin/env dotnet
// Lists interfaces and constructed collaborators in Debarr and Debarr.Tests as tab-separated rows, for the interfaces review.
//   dotnet run tools/list-interfaces.cs -- interfaces      each Debarr interface with its implementations in Debarr, its implementations in Debarr.Tests, and the types that take it as a constructor or method parameter
//   dotnet run tools/list-interfaces.cs -- constructions   each class Debarr code constructs, the type and member that construct it, and file:line
// Paths are relative to src/. Records, value types, exceptions, delegates and BCL collections are left out of constructions.
#:package Microsoft.CodeAnalysis.Workspaces.MSBuild@5.9.0
#:package Microsoft.CodeAnalysis.CSharp.Workspaces@5.9.0
#:property PublishAot=false

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;

var mode = args.FirstOrDefault() ?? "interfaces";
var source = Path.GetFullPath(Path.Combine(AppContext.GetData("EntryPointFileDirectoryPath") as string ?? ".", "..", "src"));

using var workspace = MSBuildWorkspace.Create();
var solution = await workspace.OpenSolutionAsync(Path.Combine(source, "Debarr.sln"));

var implementations = new Dictionary<string, SortedSet<string>>();
var doubles = new Dictionary<string, SortedSet<string>>();
var consumers = new Dictionary<string, SortedSet<string>>();
var interfaces = new SortedSet<string>();
var constructions = new List<(string Type, string Constructor, string Location)>();

foreach (var project in solution.Projects)
{
    var compilation = await project.GetCompilationAsync() ?? throw new InvalidOperationException(project.Name);
    var isTests = project.Name.EndsWith(".Tests", StringComparison.Ordinal);

    foreach (var tree in compilation.SyntaxTrees)
    {
        var path = tree.FilePath;
        if (!path.StartsWith(source, StringComparison.OrdinalIgnoreCase) || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
        {
            continue;
        }

        var model = compilation.GetSemanticModel(tree);
        var root = await tree.GetRootAsync();

        foreach (var declaration in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            if (model.GetDeclaredSymbol(declaration) is not INamedTypeSymbol type)
            {
                continue;
            }

            if (type.TypeKind == TypeKind.Interface && !isTests)
            {
                interfaces.Add(type.Name);
            }

            foreach (var implemented in type.Interfaces.Where(IsDebarr))
            {
                Add(isTests ? doubles : implementations, implemented.Name, type.Name);
            }

            if (isTests)
            {
                continue;
            }

            foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
            {
                foreach (var parameter in method.Parameters.Where(parameter => IsDebarr(parameter.Type) && parameter.Type.TypeKind == TypeKind.Interface))
                {
                    Add(consumers, parameter.Type.Name, method.MethodKind == MethodKind.Constructor ? type.Name : $"{type.Name}.{method.Name}");
                }
            }
        }

        if (isTests)
        {
            continue;
        }

        foreach (var creation in root.DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>())
        {
            if (model.GetTypeInfo(creation).Type is not INamedTypeSymbol created || !IsCollaborator(created))
            {
                continue;
            }

            var member = creation.Ancestors().OfType<MemberDeclarationSyntax>().FirstOrDefault(node => node is not TypeDeclarationSyntax);
            var constructor = model.GetEnclosingSymbol(creation.SpanStart) is { } enclosing
                ? $"{enclosing.ContainingType?.Name}.{(enclosing is IMethodSymbol { AssociatedSymbol: { } associated } ? associated.Name : enclosing.Name)}"
                : member?.ToString() ?? "?";
            var line = tree.GetLineSpan(creation.Span).StartLinePosition.Line + 1;
            constructions.Add((created.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat), constructor, $"{Path.GetRelativePath(source, path).Replace('\\', '/')}:{line}"));
        }
    }
}

switch (mode)
{
    case "interfaces":
        Console.WriteLine("interface\timplementations\tdoubles\tconsumers");
        foreach (var name in interfaces)
        {
            Console.WriteLine($"{name}\t{Join(implementations, name)}\t{Join(doubles, name)}\t{Join(consumers, name)}");
        }
        break;
    case "constructions":
        Console.WriteLine("type\tconstructed in\tlocation");
        foreach (var row in constructions.OrderBy(row => row.Type, StringComparer.Ordinal).ThenBy(row => row.Location, StringComparer.Ordinal))
        {
            Console.WriteLine($"{row.Type}\t{row.Constructor}\t{row.Location}");
        }
        break;
    default:
        Console.Error.WriteLine("usage: dotnet run tools/list-interfaces.cs -- [interfaces|constructions]");
        return 2;
}

return 0;

static bool IsDebarr(ITypeSymbol type) => type.ContainingNamespace?.ToDisplayString().StartsWith("Debarr", StringComparison.Ordinal) == true;

static bool IsCollaborator(INamedTypeSymbol type) =>
    type.TypeKind == TypeKind.Class
    && !type.IsRecord
    && !InheritsFrom(type, "System.Exception")
    && !InheritsFrom(type, "System.Attribute")
    && type.ContainingNamespace?.ToDisplayString() is { } space
    && !space.StartsWith("System.Collections", StringComparison.Ordinal)
    && space != "System.Text"
    && space != "System";

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

static void Add(Dictionary<string, SortedSet<string>> map, string key, string value)
{
    if (!map.TryGetValue(key, out var set))
    {
        map[key] = set = new SortedSet<string>(StringComparer.Ordinal);
    }

    set.Add(value);
}

static string Join(Dictionary<string, SortedSet<string>> map, string key) =>
    map.TryGetValue(key, out var set) ? string.Join(", ", set) : "-";
