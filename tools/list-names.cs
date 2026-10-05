#!/usr/bin/env dotnet
// Lists the names declared in Debarr and Debarr.Tests as tab-separated rows, for the names review.
//   dotnet run tools/list-names.cs -- declarations   kind, name, file:line of every declared name
//   dotnet run tools/list-names.cs -- words          each word the declared names use, its count and an example
//   dotnet run tools/list-names.cs -- arguments      each variable passed to a Debarr parameter of another name
// Paths are relative to src/. Razor markup is scanned for the locals and lambda parameters it declares.
#:package Microsoft.CodeAnalysis.Workspaces.MSBuild@5.9.0
#:package Microsoft.CodeAnalysis.CSharp.Workspaces@5.9.0
#:property PublishAot=false

using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Operations;

var mode = args.FirstOrDefault() ?? "declarations";
var source = Path.GetFullPath(Path.Combine(AppContext.GetData("EntryPointFileDirectoryPath") as string ?? ".", "..", "src"));

using var workspace = MSBuildWorkspace.Create();
var solution = await workspace.OpenSolutionAsync(Path.Combine(source, "Debarr.sln"));

var declarations = new List<(string Kind, string Name, string Location)>();
var arguments = new List<(string Argument, string Parameter, string Method, string Location)>();

foreach (var project in solution.Projects)
{
    var compilation = await project.GetCompilationAsync() ?? throw new InvalidOperationException(project.Name);

    // Unresolved symbols leave arguments unmatched, so compile errors make the lists incomplete.
    foreach (var error in compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Take(5))
    {
        Console.Error.WriteLine($"{project.Name}: {error}");
    }

    foreach (var tree in compilation.SyntaxTrees)
    {
        var path = tree.FilePath;
        if (!path.StartsWith(source, StringComparison.OrdinalIgnoreCase) || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
        {
            continue;
        }

        var model = compilation.GetSemanticModel(tree);
        foreach (var node in tree.GetRoot().DescendantNodes())
        {
            foreach (var (kind, token) in Declared(node))
            {
                declarations.Add((kind, token.ValueText, Locate(token.GetLocation())));
            }

            if (node is ArgumentSyntax argument
                && model.GetOperation(argument) is IArgumentOperation { Parameter: { } parameter }
                && parameter.Locations.Any(location => location.IsInSource)
                && IsVariable(model.GetSymbolInfo(argument.Expression).Symbol)
                && NameOf(argument.Expression) is { } name
                && Normalize(name) != Normalize(parameter.Name))
            {
                var method = parameter.ContainingSymbol.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
                arguments.Add((name, parameter.Name, method, Locate(argument.GetLocation())));
            }
        }
    }
}

foreach (var markup in Directory.EnumerateFiles(source, "*.razor", SearchOption.AllDirectories).Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
{
    var lines = File.ReadAllLines(markup);
    for (var index = 0; index < lines.Length; index++)
    {
        foreach (Match match in Regex.Matches(lines[index], @"@foreach\s*\(\s*var\s+(?<name>\w+)|Context=""(?<name>\w+)""|(?<name>\w+)\s*=>|\((?<name>\w+)\)\s*=>"))
        {
            declarations.Add(("markup", match.Groups["name"].Value, $"{Path.GetRelativePath(source, markup).Replace('\\', '/')}:{index + 1}"));
        }
    }
}

switch (mode)
{
    case "declarations":
        foreach (var row in declarations.Distinct().OrderBy(row => row.Location, StringComparer.Ordinal))
        {
            Console.WriteLine($"{row.Kind}\t{row.Name}\t{row.Location}");
        }

        break;
    case "words":
        var words = declarations
            .SelectMany(row => Words(row.Name).Select(word => (Word: word, row.Name, row.Location)))
            .GroupBy(entry => entry.Word, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var group in words)
        {
            var example = group.First();
            Console.WriteLine($"{group.Key.ToLowerInvariant()}\t{group.Count()}\t{example.Name}\t{example.Location}");
        }

        break;
    case "arguments":
        foreach (var row in arguments.Distinct().OrderBy(row => row.Location, StringComparer.Ordinal))
        {
            Console.WriteLine($"{row.Argument}\t{row.Parameter}\t{row.Method}\t{row.Location}");
        }

        break;
    default:
        Console.Error.WriteLine($"Unknown mode {mode}: use declarations, words or arguments.");
        return 1;
}

return 0;

string Locate(Location location)
{
    var span = location.GetLineSpan();
    return $"{Path.GetRelativePath(source, span.Path).Replace('\\', '/')}:{span.StartLinePosition.Line + 1}";
}

static IEnumerable<(string Kind, SyntaxToken Token)> Declared(SyntaxNode node)
{
    switch (node)
    {
        case BaseTypeDeclarationSyntax type:
            yield return ("type", type.Identifier);
            break;
        case DelegateDeclarationSyntax @delegate:
            yield return ("type", @delegate.Identifier);
            break;
        case MethodDeclarationSyntax method when method.ExplicitInterfaceSpecifier is null && !method.Modifiers.Any(SyntaxKind.OverrideKeyword):
            yield return ("method", method.Identifier);
            break;
        case PropertyDeclarationSyntax property when !property.Modifiers.Any(SyntaxKind.OverrideKeyword):
            yield return ("property", property.Identifier);
            break;
        case EventDeclarationSyntax @event:
            yield return ("event", @event.Identifier);
            break;
        case FieldDeclarationSyntax field:
            foreach (var variable in field.Declaration.Variables)
            {
                yield return ("field", variable.Identifier);
            }

            break;
        case EnumMemberDeclarationSyntax member:
            yield return ("enum member", member.Identifier);
            break;
        case ParameterSyntax parameter when parameter.Identifier.ValueText.Length > 0 && parameter.Identifier.ValueText != "_":
            yield return (parameter.Parent?.Parent is RecordDeclarationSyntax ? "record parameter" : "parameter", parameter.Identifier);
            break;
        case TypeParameterSyntax typeParameter:
            yield return ("type parameter", typeParameter.Identifier);
            break;
        case LocalDeclarationStatementSyntax local:
            foreach (var variable in local.Declaration.Variables)
            {
                yield return ("local", variable.Identifier);
            }

            break;
        case UsingStatementSyntax { Declaration: { } declaration }:
            foreach (var variable in declaration.Variables)
            {
                yield return ("local", variable.Identifier);
            }

            break;
        case ForStatementSyntax { Declaration: { } declaration }:
            foreach (var variable in declaration.Variables)
            {
                yield return ("local", variable.Identifier);
            }

            break;
        case LocalFunctionStatementSyntax function:
            yield return ("local function", function.Identifier);
            break;
        case SingleVariableDesignationSyntax designation:
            yield return ("local", designation.Identifier);
            break;
        case ForEachStatementSyntax forEach:
            yield return ("local", forEach.Identifier);
            break;
        case CatchDeclarationSyntax { Identifier.ValueText.Length: > 0 } @catch:
            yield return ("local", @catch.Identifier);
            break;
        case FromClauseSyntax from:
            yield return ("local", from.Identifier);
            break;
        case LetClauseSyntax let:
            yield return ("local", let.Identifier);
            break;
    }
}

// The name an argument passes: a local, parameter, field or property, read directly or through one member access.
static string? NameOf(ExpressionSyntax expression) => expression switch
{
    IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
    MemberAccessExpressionSyntax { Name: IdentifierNameSyntax name } => name.Identifier.ValueText,
    _ => null,
};

static bool IsVariable(ISymbol? symbol) => symbol switch
{
    ILocalSymbol or IParameterSymbol or IPropertySymbol => true,
    IFieldSymbol field => !field.IsConst && field.ContainingType.TypeKind != TypeKind.Enum,
    _ => false,
};

static string Normalize(string name) => name.TrimStart('_').ToLowerInvariant();

// Splits PascalCase, camelCase and snake_case into words, keeping acronyms such as Mqtt or IO whole.
static IEnumerable<string> Words(string name) =>
    Regex.Matches(name, @"[A-Z]+(?=[A-Z][a-z])|[A-Z]?[a-z]+|[A-Z]+|\d+")
        .Select(match => match.Value)
        .Where(word => !word.All(char.IsDigit));
