#!/usr/bin/env dotnet
// Renames symbols in Debarr and Debarr.Tests with Roslyn, so every reference, cref and named argument follows.
//   dotnet run tools/rename-symbols.cs -- renames.tsv
// Each line of the list is path:line, the old name and the new name, tab-separated, with the path relative to src/.
// It renames every declaration of the old name on that line. Blank lines and lines starting with # are skipped.
// A member of a component's code-behind is also renamed as a whole word in the component's markup.
#:package Microsoft.CodeAnalysis.Workspaces.MSBuild@5.9.0
#:package Microsoft.CodeAnalysis.CSharp.Workspaces@5.9.0
#:property PublishAot=false

using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Rename;

var source = Path.GetFullPath(Path.Combine(AppContext.GetData("EntryPointFileDirectoryPath") as string ?? ".", "..", "src"));
var renames = File.ReadAllLines(args.Single())
    .Where(line => line.Trim().Length > 0 && !line.StartsWith('#'))
    .Select(line => line.Split('\t'))
    .Select(fields => (Location: fields[0], OldName: fields[1], NewName: fields[2]))
    .ToList();

using var workspace = MSBuildWorkspace.Create();
var solution = await workspace.OpenSolutionAsync(Path.Combine(source, "Debarr.sln"));
var options = new SymbolRenameOptions(RenameOverloads: false, RenameInStrings: false, RenameInComments: false, RenameFile: false);
var markupRenames = new List<(string Markup, string OldName, string NewName)>();
var failures = 0;

foreach (var (location, oldName, newName) in renames)
{
    var separator = location.LastIndexOf(':');
    var path = Path.GetFullPath(Path.Combine(source, location[..separator]));
    var line = int.Parse(location[(separator + 1)..]) - 1;

    // Each rename makes a new solution, so the next declaration on the line is looked up in that one.
    var renamed = 0;
    while (await FindDeclarationAsync(solution, path, line, oldName) is { } symbol)
    {
        Console.WriteLine($"{location}\t{oldName} -> {newName}");
        solution = await Renamer.RenameSymbolAsync(solution, symbol, options, newName);
        renamed++;
    }

    if (renamed == 0)
    {
        Console.Error.WriteLine($"{location}: no declaration of {oldName}");
        failures++;
        continue;
    }

    var markup = path.EndsWith(".razor.cs", StringComparison.OrdinalIgnoreCase) ? path[..^3] : null;
    if (markup is not null && File.Exists(markup))
    {
        markupRenames.Add((markup, oldName, newName));
    }
}

// Each changed file keeps its own line endings and byte order mark.
foreach (var projectChanges in solution.GetChanges(workspace.CurrentSolution).GetProjectChanges())
{
    foreach (var documentId in projectChanges.GetChangedDocuments())
    {
        var document = solution.GetDocument(documentId)!;
        var original = File.ReadAllBytes(document.FilePath!);
        var hasByteOrderMark = original is [0xEF, 0xBB, 0xBF, ..];
        var newLine = System.Text.Encoding.UTF8.GetString(original).Contains("\r\n") ? "\r\n" : "\n";
        var text = (await document.GetTextAsync()).ToString().ReplaceLineEndings(newLine);
        File.WriteAllText(document.FilePath!, text, new System.Text.UTF8Encoding(hasByteOrderMark));
    }
}

foreach (var (markup, oldName, newName) in markupRenames)
{
    var text = File.ReadAllText(markup);
    var renamed = Regex.Replace(text, $@"\b{Regex.Escape(oldName)}\b", newName);
    if (renamed != text)
    {
        File.WriteAllText(markup, renamed);
        Console.WriteLine($"{Path.GetRelativePath(source, markup).Replace('\\', '/')}\t{oldName} -> {newName}");
    }
}

return failures == 0 ? 0 : 1;

static async Task<ISymbol?> FindDeclarationAsync(Solution solution, string path, int line, string name)
{
    foreach (var document in solution.Projects.SelectMany(project => project.Documents).Where(document => string.Equals(document.FilePath, path, StringComparison.OrdinalIgnoreCase)))
    {
        var root = await document.GetSyntaxRootAsync() ?? throw new InvalidOperationException(path);
        var model = await document.GetSemanticModelAsync() ?? throw new InvalidOperationException(path);
        var span = (await document.GetTextAsync()).Lines[line].Span;
        foreach (var token in root.DescendantTokens(span).Where(token => token.ValueText == name))
        {
            if (token.Parent is { } parent && model.GetDeclaredSymbol(parent) is { } declared && declared.Name == name)
            {
                return declared;
            }
        }
    }

    return null;
}
