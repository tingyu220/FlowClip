using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FlowClip.App.ViewModels;

namespace FlowClip.App.Views;

/// <summary>
/// 剪贴板历史面板窗口。
/// 仅在用户主动调用（全局快捷键 / 托盘）时显示；关闭按钮与 Esc 仅隐藏，不退出程序。
/// </summary>
public partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    /// <summary>请求隐藏面板（关闭按钮 / Esc，由 App 处理）</summary>
    public event EventHandler? RequestedHide;

    /// <summary>
    /// 请求「提交选中项」：已写入剪贴板，收起面板并尝试粘贴（由 App 处理）。
    /// 与 RequestedHide 分开，避免 Esc 也被当作「粘贴」操作。
    /// </summary>
    public event EventHandler? RequestedCommit;

    /// <summary>请求最小化到托盘（由 App 处理）</summary>
    public event EventHandler? RequestedMinimize;

    /// <summary>请求切换深浅色主题（由 App 处理）</summary>
    public event EventHandler? RequestedToggleTheme;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = viewModel;

        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>打开面板时调用：保证有选中项并把焦点交给搜索框。</summary>
    public void FocusSearch()
    {
        ViewModel.EnsureSelection();
        SearchBox.Focus();
        SearchBox.SelectAll();
        ScrollToSelected();
    }

    private void ScrollToSelected()
    {
        if (ClipList.SelectedItem is not null)
        {
            ClipList.ScrollIntoView(ClipList.SelectedItem);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                RequestedHide?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
            case Key.Enter:
                // Enter：写入剪贴板 → 收起面板 → 由 App 决定是否代为粘贴
                ViewModel.CopySelectedCommand.Execute(null);
                RequestedCommit?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
            case Key.Delete:
                ViewModel.DeleteSelectedCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.P when Keyboard.Modifiers == ModifierKeys.None:
                ViewModel.TogglePinCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Up:
                if (ViewModel.SelectedIndex > 0)
                {
                    ViewModel.SelectedIndex--;
                    ScrollToSelected();
                }
                e.Handled = true;
                break;
            case Key.Down:
                if (ViewModel.SelectedIndex < ViewModel.Items.Count - 1)
                {
                    ViewModel.SelectedIndex++;
                    ScrollToSelected();
                }
                e.Handled = true;
                break;
        }
        // 注意：不拦截 Ctrl+C / Ctrl+V，确保系统复制粘贴语义不被破坏
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
        => RequestedHide?.Invoke(this, EventArgs.Empty);

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
        => RequestedMinimize?.Invoke(this, EventArgs.Empty);

    private void OnToggleThemeClick(object sender, RoutedEventArgs e)
        => RequestedToggleTheme?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// 从标题栏（以及底部提示条）拖动移动窗口。
    /// 窗口无系统标题栏，且 WindowChrome 的 CaptionHeight=0，所以需要手动处理。
    /// 点在标题栏按钮上时不能拖动，否则会挡住主题 / 最小化 / 关闭三个按钮。
    /// </summary>
    private void OnHeaderDrag(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindAncestor<Button>(source) is not null)
        {
            return;
        }

        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (Exception)
        {
            // 窗口已隐藏或处于异常状态时 DragMove 可能抛异常，忽略即可
        }
    }

    /// <summary>在可视化树中向上查找指定类型的祖先。</summary>
    private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T typed)
            {
                return typed;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
