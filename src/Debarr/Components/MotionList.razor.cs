using System.Reactive.Disposables;
using Microsoft.AspNetCore.Components;

namespace Debarr.Components;

/// <summary>
/// A keyed list whose items animate as they join it and stay for a moment to animate as they leave it.
/// The items it first shows, and the items that stay, render without motion.
/// Each item's root element takes the entry's <see cref="MotionEntry{TItem}.Class"/>.
/// </summary>
public sealed partial class MotionList<TItem>(TimeProvider timeProvider) : ComponentBase, IDisposable
{
    /// <summary>How long a leaving item stays, which matches its leave animation.</summary>
    public static readonly TimeSpan LeaveDuration = TimeSpan.FromMilliseconds(200);

    private readonly CancellationDisposable _cancellation = new();
    private IReadOnlyList<TItem>? _items;
    private List<MotionEntry<TItem>> _entries = [];
    private int _leaveVersion;

    /// <summary>The items in order; a new list instance is a new set of items.</summary>
    [Parameter, EditorRequired]
    public IReadOnlyList<TItem> Items { get; set; } = [];

    /// <summary>Names each item, unique within the list and stable across lists.</summary>
    [Parameter, EditorRequired]
    public Func<TItem, object> Key { get; set; } = default!;

    [Parameter]
    public RenderFragment<MotionEntry<TItem>>? ChildContent { get; set; }

    /// <summary>Whether the list is a table body whose items are rows, which fade, rather than blocks, which also grow and shrink.</summary>
    [Parameter]
    public bool TableBody { get; set; }

    [Parameter]
    public string? Class { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    private string EnterClass => TableBody ? "motion-row-enter" : "motion-enter";

    private string LeaveClass => TableBody ? "motion-row-leave" : "motion-leave";

    protected override void OnParametersSet()
    {
        if (ReferenceEquals(Items, _items))
        {
            return;
        }

        var isFirst = _items is null;
        _items = Items;
        var previous = _entries.ToDictionary(entry => entry.Key);
        var keys = new HashSet<object>();
        var entries = new List<MotionEntry<TItem>>(Items.Count);
        foreach (var item in Items)
        {
            var key = Key(item);
            keys.Add(key);
            var enters = !isFirst && (!previous.TryGetValue(key, out var entry) || entry.IsLeaving);
            entries.Add(new MotionEntry<TItem>(item, key, enters ? EnterClass : null));
        }

        // A leaving item keeps its place after the item it followed.
        var anchor = -1;
        var anyLeft = false;
        foreach (var entry in _entries)
        {
            if (keys.Contains(entry.Key))
            {
                anchor = entries.FindIndex(merged => Equals(merged.Key, entry.Key));
                continue;
            }

            anyLeft |= !entry.IsLeaving;
            entries.Insert(++anchor, entry.IsLeaving ? entry : entry with { Class = LeaveClass, IsLeaving = true });
        }

        _entries = entries;
        if (anyLeft)
        {
            _ = RemoveLeftEntriesAsync(++_leaveVersion);
        }
    }

    /// <summary>Drops the leaving items once the newest of them has animated.</summary>
    private async Task RemoveLeftEntriesAsync(int leaveVersion)
    {
        try
        {
            await Task.Delay(LeaveDuration, timeProvider, _cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await InvokeAsync(() =>
        {
            if (leaveVersion != _leaveVersion || _cancellation.IsDisposed)
            {
                return;
            }

            _entries = [.. _entries.Where(entry => !entry.IsLeaving)];
            StateHasChanged();
        });
    }

    public void Dispose() => _cancellation.Dispose();
}
