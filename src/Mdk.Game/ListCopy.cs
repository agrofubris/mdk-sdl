namespace Mdk.Game;

/// <summary>A copy of a list to go through while the list changes (objects spawned or removed on
/// the way), its storage taken from a pool and given back on <see cref="Dispose"/>: no list per tick.
/// <code>
///   using var objects = ListCopy&lt;MdkObject&gt;.Of(runtime.Objects);
///   foreach (var obj in objects) { ... runtime.Objects.Remove(obj) ... }
/// </code></summary>
public readonly struct ListCopy<T> : IDisposable
{
    /// <summary>Per thread: parallel tests run several levels at once.</summary>
    [ThreadStatic]
    private static Stack<List<T>>? _pool;

    private readonly List<T> _items;

    private ListCopy(List<T> items) => _items = items;

    public static ListCopy<T> Of(List<T> source)
    {
        _pool ??= new Stack<List<T>>();
        var items = _pool.Count > 0 ? _pool.Pop() : [];
        items.AddRange(source);
        return new ListCopy<T>(items);
    }

    public List<T>.Enumerator GetEnumerator() => _items.GetEnumerator();

    public void Dispose()
    {
        _items.Clear();
        _pool ??= new Stack<List<T>>();
        _pool.Push(_items);
    }
}
