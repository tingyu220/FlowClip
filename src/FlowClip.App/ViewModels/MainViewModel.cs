using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using FlowClip.App.Converters;
using FlowClip.Core.Models;
using FlowClip.Core.Services;

namespace FlowClip.App.ViewModels;

/// <summary>
/// 筛选选项（全部 / 各类型）。
/// </summary>
public class FilterOption
{
    public string Label { get; }
    public ClipItemType? Type { get; }

    public FilterOption(string label, ClipItemType? type)
    {
        Label = label;
        Type = type;
    }
}

/// <summary>
/// 历史面板主视图模型：负责搜索、筛选、选中态与操作命令。
/// 保持轻量 MVVM，仅暴露界面所需的聚合状态。
/// </summary>
public class MainViewModel : INotifyPropertyChanged
{
    private readonly ClipHistoryService _service;
    private string _searchQuery = string.Empty;
    private ClipItemType? _activeFilter;
    private int _selectedIndex = -1;

    /// <summary>当前筛选后的可见条目（用于列表绑定）</summary>
    public ObservableCollection<ClipItem> Items { get; }

    public List<FilterOption> FilterOptions { get; }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (_searchQuery == value) return;
            _searchQuery = value;
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    public ClipItemType? ActiveFilter
    {
        get => _activeFilter;
        set
        {
            if (_activeFilter == value) return;
            _activeFilter = value;
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (_selectedIndex == value) return;
            _selectedIndex = value;
            OnPropertyChanged();
        }
    }

    public ClipItem? SelectedItem =>
        (SelectedIndex >= 0 && SelectedIndex < Items.Count) ? Items[SelectedIndex] : null;

    public ICommand CopySelectedCommand { get; }
    public ICommand DeleteSelectedCommand { get; }
    public ICommand TogglePinCommand { get; }
    public ICommand SetFilterCommand { get; }
    public ICommand ClearSearchCommand { get; }

    /// <summary>请求将某条目写入系统剪贴板（由 App 层执行真实写入）</summary>
    public event EventHandler<ClipItem>? ItemCopied;

    /// <summary>请求关闭面板</summary>
    public event EventHandler? RequestClose;

    public MainViewModel(ClipHistoryService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        Items = new ObservableCollection<ClipItem>(_service.GetFiltered(null, null));

        FilterOptions = new List<FilterOption>
        {
            new("全部", null),
            new("文字", ClipItemType.Text),
            new("图片", ClipItemType.Image),
            new("链接", ClipItemType.Link),
            new("文件", ClipItemType.File),
            new("文件夹", ClipItemType.Folder),
            new("多文件", ClipItemType.MultiFile),
        };

        CopySelectedCommand = new RelayCommand(_ => CopySelected());
        DeleteSelectedCommand = new RelayCommand(_ => DeleteSelected());
        TogglePinCommand = new RelayCommand(_ => TogglePin());
        SetFilterCommand = new RelayCommand(p => SetFilter(p));
        ClearSearchCommand = new RelayCommand(_ => SearchQuery = string.Empty);
    }

    private void ApplyFilter()
    {
        var filtered = _service.GetFiltered(SearchQuery, ActiveFilter).ToList();
        Items.Clear();
        foreach (var item in filtered)
        {
            Items.Add(item);
        }

        if (SelectedIndex >= Items.Count)
        {
            SelectedIndex = Items.Count - 1;
        }
    }

    /// <summary>确保有选中项（打开面板时调用）</summary>
    public void EnsureSelection()
    {
        if (SelectedIndex < 0 && Items.Count > 0)
        {
            SelectedIndex = 0;
        }
    }

    private void CopySelected()
    {
        var item = SelectedItem;
        if (item is null) return;
        ItemCopied?.Invoke(this, item);
    }

    private void DeleteSelected()
    {
        var item = SelectedItem;
        if (item is null) return;
        _service.Remove(item.Id);
        ApplyFilter();
        EnsureSelection();
    }

    private void TogglePin()
    {
        var item = SelectedItem;
        if (item is null) return;
        _service.TogglePin(item.Id);
        ApplyFilter();
        var idx = Items.IndexOf(item);
        if (idx >= 0) SelectedIndex = idx;
    }

    private void SetFilter(object? parameter)
    {
        var type = parameter as ClipItemType?;
        // 再次点击同一筛选则取消，回到“全部”
        ActiveFilter = (ActiveFilter == type) ? null : type;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
