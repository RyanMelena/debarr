namespace Debarr.Components;

/// <summary>A column a table sorts by, with the words for each direction, such as Oldest First and Newest First.</summary>
/// <param name="Value">The sort label, as the URL and the column header name it.</param>
public sealed record SortOption(string Value, string Label, string AscendingText, string DescendingText);
