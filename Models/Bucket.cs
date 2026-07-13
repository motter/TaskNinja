using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TaskNinja.Models;

/// <summary>
/// A named container for tasks. Every task belongs to exactly one
/// bucket; "All" is a UI filter, not a bucket. Buckets are minimal —
/// just an ID and a name — to keep the data layout simple.
///
/// The default bucket (created on first run if none exist) has the
/// well-known Id "default" and the display name "Tasks". Users can
/// rename it but not delete it (the persistence layer keeps the last
/// bucket undeletable to avoid empty-state edge cases).
/// </summary>
public class Bucket : INotifyPropertyChanged
{
    public const string DefaultBucketId = "default";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    private string _name = "";
    public string Name
    {
        get => _name;
        set { _name = value ?? ""; Notify(); }
    }

    /// <summary>Order in the bucket dropdown (lower first). Default bucket
    /// always sorts first regardless of this value.</summary>
    public int SortOrder { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name ?? ""));
}
