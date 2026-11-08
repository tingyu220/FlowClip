using System;
using System.Collections.ObjectModel;
using System.Linq;
using FlowClip.Core.Models;

namespace FlowClip.Core.Services;

/// <summary>
/// 剪贴板历史存储与服务（内存版）。
/// 初期使用模拟数据；真实剪贴板监听仅做独立技术验证，不向此处写入。
/// </summary>
public class ClipHistoryService
{
    private readonly ObservableCollection<ClipItem> _items = new();
    private readonly object _sync = new();

    /// <summary>对外只读的历史集合（置顶项始终在最前，其余按时间倒序）</summary>
    public ReadOnlyObservableCollection<ClipItem> Items { get; }

    public ClipHistoryService()
    {
        Items = new ReadOnlyObservableCollection<ClipItem>(_items);
    }

    /// <summary>新增一条记录并维持排序</summary>
    public void Add(ClipItem item)
    {
        if (item is null) return;
        lock (_sync)
        {
            _items.Insert(0, item);
            SortItems();
        }
    }

    /// <summary>按 Id 删除</summary>
    public bool Remove(string id)
    {
        lock (_sync)
        {
            var target = _items.FirstOrDefault(x => x.Id == id);
            return target is not null && _items.Remove(target);
        }
    }

    /// <summary>切换置顶状态</summary>
    public void TogglePin(string id)
    {
        lock (_sync)
        {
            var target = _items.FirstOrDefault(x => x.Id == id);
            if (target is null) return;
            target.IsPinned = !target.IsPinned;
            SortItems();
        }
    }

    /// <summary>清空全部</summary>
    public void ClearAll()
    {
        lock (_sync) _items.Clear();
    }

    /// <summary>按 Id 获取</summary>
    public ClipItem? GetById(string id)
    {
        lock (_sync) return _items.FirstOrDefault(x => x.Id == id);
    }

    /// <summary>
    /// 按关键字与类型筛选。
    /// 关键字匹配标题 / 预览 / 来源（忽略大小写）；type 为 null 表示全部类型。
    /// 保持与 Items 相同的排序（置顶优先、时间倒序）。
    /// </summary>
    public System.Collections.Generic.IEnumerable<ClipItem> GetFiltered(string? query, ClipItemType? type)
    {
        lock (_sync)
        {
            var q = string.IsNullOrWhiteSpace(query) ? null : query.Trim().ToLowerInvariant();
            return _items
                .Where(x =>
                    (!type.HasValue || x.Type == type.Value) &&
                    (q is null ||
                     (x.Title?.ToLowerInvariant().Contains(q) ?? false) ||
                     (x.Preview?.ToLowerInvariant().Contains(q) ?? false) ||
                     (x.SourceApp?.ToLowerInvariant().Contains(q) ?? false)))
                .ToList();
        }
    }

    private void SortItems()
    {
        var ordered = _items
            .OrderByDescending(x => x.IsPinned)
            .ThenByDescending(x => x.Timestamp)
            .ToList();

        for (int i = 0; i < ordered.Count; i++)
        {
            int current = _items.IndexOf(ordered[i]);
            if (current != i && current >= 0)
            {
                _items.Move(current, i);
            }
        }
    }
}
