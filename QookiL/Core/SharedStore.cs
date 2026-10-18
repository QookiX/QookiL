namespace QookiL.Core;

/// <summary>线程安全的共享列表。UI 线程增删，网页服务器线程读。</summary>
public sealed class SharedStore
{
    private readonly object _gate = new();
    private readonly List<SharedItem> _items = new();

    /// <summary>共享列表的版本号：每次增删改都会 +1，网页端靠轮询它来实时刷新。</summary>
    public int Revision { get; private set; }

    public IReadOnlyList<SharedItem> Snapshot()
    {
        lock (_gate) return _items.ToList();
    }

    /// <summary>添加一个共享项；若同一路径已存在则返回 false。</summary>
    public bool Add(SharedItem item)
    {
        lock (_gate)
        {
            if (_items.Any(i => string.Equals(i.FullPath, item.FullPath, StringComparison.OrdinalIgnoreCase)))
                return false;
            _items.Add(item);
            Revision++;
            return true;
        }
    }

    public bool Remove(Guid id)
    {
        lock (_gate)
        {
            if (_items.RemoveAll(i => i.Id == id) > 0)
            {
                Revision++;
                return true;
            }
            return false;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            if (_items.Count > 0) Revision++;
            _items.Clear();
        }
    }

    public SharedItem? Find(Guid id)
    {
        lock (_gate) return _items.FirstOrDefault(i => i.Id == id);
    }
}
