namespace Debarr.Components;

/// <summary>An item of a <see cref="MotionList{TItem}"/>, with the class its root element takes while it enters or leaves.</summary>
public sealed record MotionEntry<TItem>(TItem Item, object Key, string? Class)
{
    /// <summary>Whether the item has left the list and stays only for its leave animation.</summary>
    public bool IsLeaving { get; init; }
}
