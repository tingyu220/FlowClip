using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FlowClip.App.Converters;
using FlowClip.App.Infrastructure;
using FlowClip.App.ViewModels;
using FlowClip.App.Views;
using FlowClip.Core.MockData;
using FlowClip.Core.Models;
using FlowClip.Core.Services;
using FlowClip.Windows.Acrylic;
using FlowClip.Windows.Clipboard;
using FlowClip.Windows.Hotkey;
using FlowClip.Windows.Native;
using FlowClip.Windows.Theme;
using FlowClip.Windows.Tray;

namespace FlowClip.App;

/// <summary>
/// FlowClip 应用入口。
/// 负责装配服务（剪贴板监听 / 全局快捷键 / 系统托盘）、主题管理，并支持自测与截图渲染模式。
/// 仅“主动调用”才会显示历史面板；普通复制静默运行。
/// </summary>
public partial class App : Application
{
    // 全局快捷键：Ctrl+Shift+V（绝不占用 Ctrl+C / Ctrl+V）
    private const uint HotkeyVk = 0x56; // 'V'

    private MessageWindow? _messageWindow;
    private ClipboardMonitor? _monitor;
    private HotkeyManager? _hotkey;
    private TrayManager? _tray;

    private ClipHistoryService? _service;
    private MainWindow? _mainWindow;
    private ScreenshotPreviewWindow? _preview;

    private bool _isDark;
    private string _logDir = string.Empty;
    private bool _hotkeyRegistered;
    private AppSettings _settings = AppSettings.Load();
    private IntPtr _foregroundBeforeShow = IntPtr.Zero;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 托盘类应用：仅在显式调用时退出，避免无可见窗口即被关闭
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlowClip", "logs");
        Directory.CreateDirectory(_logDir);

        // 主题：若用户曾保存偏好则沿用，否则跟随系统
        _isDark = _settings.RememberTheme ? _settings.IsDark : ThemeHelper.IsSystemDarkMode();

        // 数据：先用模拟数据填充历史（真实监听只做独立验证，不写入此处）
        _service = new ClipHistoryService();
        foreach (var item in MockDataFactory.CreateSampleItems())
        {
            _service.Add(item);
        }

        var viewModel = new MainViewModel(_service);

        // 原生消息窗口（必须在 UI 线程创建，由 WPF Dispatcher 分发消息）
        _messageWindow = new MessageWindow();

        // 剪贴板监听：独立技术验证，写入本地日志（静默）
        _monitor = new ClipboardMonitor(_messageWindow, _logDir);

        // 全局快捷键
        _hotkey = new HotkeyManager(_messageWindow);
        _hotkeyRegistered = _hotkey.Register(
            HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.NoRepeat, HotkeyVk);
        if (!_hotkeyRegistered)
        {
            Trace.WriteLine("全局快捷键注册失败（可能被占用）");
        }

        // 系统托盘
        _tray = new TrayManager(_messageWindow, "FlowClip 剪贴板历史");

        // 窗口
        _mainWindow = new MainWindow(viewModel);
        _preview = new ScreenshotPreviewWindow();
        ApplySavedSize(_mainWindow);

        // 事件装配
        _hotkey.HotkeyPressed += (_, _) => ToggleMainWindow();
        _tray.OpenRequested += (_, _) => ShowMainWindow();
        _tray.ContextMenuRequested += (_, _) => ShowTrayMenu();
        _mainWindow.RequestedHide += (_, _) => HideMainWindow();
        _mainWindow.RequestedMinimize += (_, _) => MinimizeMainWindow();
        _mainWindow.RequestedToggleTheme += (_, _) => ToggleTheme();
        _mainWindow.RequestedCommit += (_, _) => CommitSelection();
        _mainWindow.ViewModel.ItemCopied += OnItemCopied;
        _preview.RequestedHide += (_, _) => _preview.Hide();

        // 处理命令行参数
        if (HasArg(e.Args, "--selftest"))
        {
            RunSelfTest();
            return;
        }

        if (HasArg(e.Args, "--capture"))
        {
            RunCapture();
            Shutdown(0);
            return;
        }

        if (HasArg(e.Args, "--bench"))
        {
            RunBenchmark();
            return;
        }

        if (HasArg(e.Args, "--realcapture"))
        {
            RunRealCapture();
            return;
        }

        if (HasArg(e.Args, "--trayshot"))
        {
            RunTrayShot();
            return;
        }

        if (HasArg(e.Args, "--dragtest"))
        {
            RunDragTest();
            return;
        }

        // 常规 GUI 模式：仅显示托盘，历史面板留待主动唤出
        ApplyTheme(_isDark);
        MaybeShowTrayHint();

        if (HasArg(e.Args, "--show"))
        {
            // 直接显示主面板：用于真实桌面截图验收（离屏渲染无法体现 DWM 圆角 / 亚克力行为）
            ShowMainWindow();
        }
    }

    /// <summary>
    /// 首次运行时弹一次气泡，告诉用户图标已驻留托盘。
    /// Windows 常把新托盘图标收进「隐藏的图标」溢出区，否则用户会以为图标没出现。
    /// </summary>
    private void MaybeShowTrayHint()
    {
        if (_settings.TrayHintShown) return;

        _settings.TrayHintShown = true;
        _settings.Save();

        // 稍晚一点再弹，确保托盘项已注册完成
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _tray?.ShowBalloon("FlowClip 已驻留系统托盘",
                "按 Ctrl+Shift+V 唤出历史面板；若图标被收进「隐藏的图标」，可把它拖到托盘上。");
        };
        timer.Start();
    }

    // ---------------- 窗口显示控制 ----------------

    private void ToggleMainWindow()
    {
        if (_mainWindow is null) return;
        if (_mainWindow.IsVisible)
        {
            HideMainWindow();
        }
        else
        {
            ShowMainWindow();
        }
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null) return;
        ApplySavedSize(_mainWindow);
        _mainWindow.ShowInTaskbar = true;
        _mainWindow.WindowState = WindowState.Normal;
        ApplyWindowChrome(_mainWindow);
        // 记录当前前台窗口，关闭面板时尝试把焦点还回去
        try { _foregroundBeforeShow = Win32.GetForegroundWindow(); } catch { /* 忽略 */ }
        _mainWindow.Show();
        _mainWindow.Activate();
        _mainWindow.FocusSearch();
    }

    /// <summary>
    /// 「提交」选中条目：面板中的 Enter。
    /// 流程＝写剪贴板（已由 CopySelectedCommand 完成）→ 收起面板并交还焦点 → 尝试代粘贴。
    /// 失效条目不写入剪贴板，直接提示；无法可靠粘贴时降级为复制 + 提示手动 Ctrl+V。
    /// </summary>
    private async void CommitSelection()
    {
        var item = _mainWindow?.ViewModel.SelectedItem;

        if (item is not null && MissingVisibilityConverter.IsMissing(item))
        {
            _tray?.ShowBalloon("无法粘贴", "源文件已不存在，这条记录已失效。");
            HideMainWindow();
            return;
        }

        HideMainWindow();
        await TryAutoPasteAsync();
    }

    /// <summary>
    /// 尝试向唤出面板前的前台窗口投递 Ctrl+V。
    /// 只在目标窗口确定有效且能激活时才代劳，否则降级提示，避免内容输入到错误窗口。
    /// </summary>
    private async Task TryAutoPasteAsync()
    {
        var target = _foregroundBeforeShow;
        if (target == IntPtr.Zero) return;

        // 给目标窗口一点时间完成前台切换，否则 SendInput 可能落到错误的窗口
        await Task.Delay(90);

        bool ok = false;
        try { ok = PasteHelper.TrySendCtrlV(target); }
        catch (Exception ex) { Trace.WriteLine("自动粘贴失败: " + ex.Message); }

        if (!ok)
        {
            _tray?.ShowBalloon("已复制", "无法自动粘贴，请在目标窗口按 Ctrl+V。");
        }
    }

    private void HideMainWindow()
    {
        if (_mainWindow is null) return;
        _mainWindow.Hide();
        _mainWindow.ShowInTaskbar = false;
        SaveSettings();
        RestoreForeground();
    }

    private void MinimizeMainWindow()
    {
        if (_mainWindow is null) return;
        // 最小化：保留后台运行，并保留任务栏入口以便恢复
        _mainWindow.ShowInTaskbar = true;
        _mainWindow.WindowState = WindowState.Minimized;
        SaveSettings();
    }

    private void ShowPreview()
    {
        if (_preview is null) return;

        // 预览内容来自本地 mock 素材（真实解码一次缩略图管线）
        var shot = Path.Combine(MockAssets.ImagesDir, "设计稿_v3.bmp");
        _preview.SetPreview(shot, "FlowClip 截图 · 240 × 150");

        ApplyWindowChrome(_preview);
        _preview.Show();
        // SizeToContent 窗口需先完成布局才能算准右上角坐标
        _preview.PositionTopRight();
    }

    /// <summary>
    /// 为窗口应用沉浸式暗色模式、原生圆角与亚克力材质（不支持时回退到半透明纯色面板）。
    /// </summary>
    private void ApplyWindowChrome(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        ThemeHelper.SetImmersiveDarkMode(hwnd, _isDark);
        ThemeHelper.SetRoundedCorners(hwnd, true);
        // 深色模式下 DWM 默认边框是纯黑色，会在圆角弧线上留下细黑圈
        ThemeHelper.ApplyBorderColor(hwnd, _isDark);
        var tint = ThemeHelper.AcrylicTint(_isDark);
        bool acrylicOk = AcrylicHelper.EnableAcrylic(hwnd, tint);
        Trace.WriteLine(acrylicOk ? "亚克力已启用" : "亚克力不可用，已回退半透明面板");
    }

    /// <summary>把记忆的窗口尺寸应用到窗口（限制在最小/最大与可用工作区之内）。</summary>
    private void ApplySavedSize(Window window)
    {
        var wa = SystemParameters.WorkArea;
        double w = Math.Clamp(_settings.WindowWidth, window.MinWidth, Math.Min(900, wa.Width));
        double h = Math.Clamp(_settings.WindowHeight, window.MinHeight, Math.Min(700, wa.Height));
        window.Width = w;
        window.Height = h;
    }

    /// <summary>持久化窗口尺寸与主题偏好。</summary>
    private void SaveSettings()
    {
        if (_mainWindow is not null)
        {
            _settings.WindowWidth = _mainWindow.Width;
            _settings.WindowHeight = _mainWindow.Height;
        }
        _settings.IsDark = _isDark;
        _settings.RememberTheme = true;
        _settings.Save();
    }

    /// <summary>关闭面板后尝试把焦点还给此前的前台窗口。</summary>
    private void RestoreForeground()
    {
        if (_foregroundBeforeShow != IntPtr.Zero)
        {
            try { Win32.SetForegroundWindow(_foregroundBeforeShow); } catch { /* 忽略 */ }
        }
    }

    private void ToggleTheme()
    {
        _isDark = !_isDark;
        ApplyTheme(_isDark);
        if (_mainWindow is { IsVisible: true }) ApplyWindowChrome(_mainWindow);
        if (_preview is { IsVisible: true }) ApplyWindowChrome(_preview);
    }

    private void ApplyTheme(bool dark)
    {
        const string light = "Resources/ThemeLight.xaml";
        const string darkTheme = "Resources/ThemeDark.xaml";

        var merged = Resources.MergedDictionaries;
        for (int i = merged.Count - 1; i >= 0; i--)
        {
            var src = merged[i].Source?.OriginalString;
            if (src is not null && (src.EndsWith("ThemeLight.xaml") || src.EndsWith("ThemeDark.xaml")))
            {
                merged.RemoveAt(i);
            }
        }

        merged.Add(new ResourceDictionary { Source = new Uri(dark ? darkTheme : light, UriKind.Relative) });
    }

    private void ShowTrayMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu
        {
            Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint,
        };

        var open = new System.Windows.Controls.MenuItem { Header = "打开历史面板" };
        open.Click += (_, _) => ShowMainWindow();
        var preview = new System.Windows.Controls.MenuItem { Header = "截图预览" };
        preview.Click += (_, _) => ShowPreview();
        var theme = new System.Windows.Controls.MenuItem
        {
            Header = _isDark ? "切换到浅色" : "切换到深色",
        };
        theme.Click += (_, _) => ToggleTheme();
        var exit = new System.Windows.Controls.MenuItem { Header = "退出" };
        exit.Click += (_, _) => ExitApp();

        menu.Items.Add(open);
        menu.Items.Add(preview);
        menu.Items.Add(theme);
        menu.Items.Add(exit);
        menu.IsOpen = true;
    }

    private void ExitApp()
    {
        SaveSettings();
        Cleanup();
        Shutdown(0);
    }

    private void Cleanup()
    {
        _tray?.Dispose();
        _hotkey?.Dispose();
        _monitor?.Dispose();
        _messageWindow?.Dispose();
    }

    // ---------------- 复制历史条目到剪贴板 ----------------

    private void OnItemCopied(object? sender, ClipItem item)
    {
        try
        {
            switch (item.Type)
            {
                case ClipItemType.Text:
                case ClipItemType.Link:
                    // 链接复制其地址（Title 即 URL）；文字复制内容
                    Clipboard.SetText(item.Title);
                    break;
                case ClipItemType.Image:
                    // 真实模式：若 ImagePath 为有效图片则 SetImage；
                    // 此处为模拟数据，复制文件名作为回退，不误判为截图
                    Clipboard.SetText(item.Title);
                    break;
                case ClipItemType.File:
                case ClipItemType.Folder:
                case ClipItemType.MultiFile:
                    // 仅保存路径引用（不复制文件本体）
                    var drop = new StringCollection();
                    if (item.FilePaths is not null)
                    {
                        foreach (var path in item.FilePaths) drop.Add(path);
                    }

                    if (drop.Count > 0) Clipboard.SetFileDropList(drop);
                    break;
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine("写入剪贴板失败: " + ex.Message);
        }
    }

    // ---------------- 自测模式 ----------------

    private void RunSelfTest()
    {
        // 第一步：稍候后设置剪贴板，触发 WM_CLIPBOARDUPDATE
        var step1 = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(400),
        };
        step1.Tick += (_, _) =>
        {
            step1.Stop();
            try { Clipboard.SetText("FlowClip 自检测试文本 " + Guid.NewGuid()); }
            catch (Exception ex) { Trace.WriteLine("自测设置剪贴板失败: " + ex.Message); }

            // 第二步：等待消息分发后检查结果
            var step2 = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(900),
            };
            step2.Tick += (_, _) =>
            {
                step2.Stop();
                var logPath = Path.Combine(_logDir, "clipboard.log");
                bool detected = File.Exists(logPath) &&
                                File.ReadAllText(logPath).Contains("剪贴板变化");

                var process = Process.GetCurrentProcess();
                long mem = process.WorkingSet64;

                var result = $"clipboard_detected={detected}{Environment.NewLine}" +
                             $"hotkey_registered={_hotkeyRegistered}{Environment.NewLine}" +
                             $"memory_workingset_bytes={mem}{Environment.NewLine}" +
                             $"memory_workingset_mb={mem / 1024.0 / 1024.0:0.00}{Environment.NewLine}" +
                             $"dark_mode={_isDark}{Environment.NewLine}" +
                             $"mock_items={_service?.Items.Count ?? 0}{Environment.NewLine}";
                File.WriteAllText(Path.Combine(_logDir, "selftest.result"), result);

                Cleanup();
                Shutdown(detected ? 0 : 1);
            };
            step2.Start();
        };
        step1.Start();
    }

    // ---------------- 截图渲染模式（真实 WPF 离屏渲染） ----------------

    private void RunCapture()
    {
        const string dir = "docs/screenshots";

        // 主面板：浅色 / 深色
        RenderMainShot(Path.Combine(dir, "main-dark.png"), 600, 480, dark: true, filter: null);
        RenderMainShot(Path.Combine(dir, "main-light.png"), 600, 480, dark: false, filter: null);

        // 分类视图：用于验证系统文件图标 / 数量角标与失效标记 / 真实图片缩略图
        RenderMainShot(Path.Combine(dir, "files-light.png"), 600, 480, dark: false, filter: ClipItemType.File);
        RenderMainShot(Path.Combine(dir, "multi-light.png"), 600, 480, dark: false, filter: ClipItemType.MultiFile);
        RenderMainShot(Path.Combine(dir, "images-light.png"), 600, 480, dark: false, filter: ClipItemType.Image);

        RenderPreviewShot(Path.Combine(dir, "preview-dark.png"), dark: true);
        RenderPreviewShot(Path.Combine(dir, "preview-light.png"), dark: false);
    }

    private void RenderMainShot(string path, int width, int height, bool dark, ClipItemType? filter)
    {
        ApplyTheme(dark);
        var vm = new MainViewModel(_service!)
        {
            ActiveFilter = filter
        };
        vm.EnsureSelection();
        var window = new MainWindow(vm);
        var content = (FrameworkElement)window.Content;

        Layout(content, new Size(width, height));
        // 图片缩略图走异步绑定，先泵几轮让它们真正到位再截图，避免拍到空占位
        PumpAsyncBindings();
        Layout(content, new Size(width, height));
        SavePng(content, path, width, height, dark);
    }

    private void RenderPreviewShot(string path, bool dark)
    {
        ApplyTheme(dark);
        var preview = new ScreenshotPreviewWindow();
        preview.SetPreview(Path.Combine(MockAssets.ImagesDir, "设计稿_v3.bmp"),
            "FlowClip 截图 · 240 × 150");

        var content = (FrameworkElement)preview.Content;
        Layout(content, new Size(280, 520));
        PumpAsyncBindings();
        var natural = content.DesiredSize;
        Layout(content, new Size(Math.Max(1, Math.Ceiling(natural.Width)),
                                 Math.Max(1, Math.Ceiling(natural.Height))));

        int w = (int)Math.Ceiling(content.DesiredSize.Width);
        int h = (int)Math.Ceiling(content.DesiredSize.Height);
        SavePng(content, path, w, h, dark);
    }

    private static void Layout(FrameworkElement content, Size size)
    {
        content.Measure(size);
        content.Arrange(new Rect(0, 0, size.Width, size.Height));
        content.UpdateLayout();
    }

    /// <summary>泵若干轮 Dispatcher，让 IsAsync 的缩略图绑定完成。</summary>
    private static void PumpAsyncBindings()
    {
        for (int i = 0; i < 40; i++)
        {
            DoEvents();
            Thread.Sleep(10);
        }
    }

    private static void DoEvents()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
            new DispatcherOperationCallback(_ =>
            {
                frame.Continue = false;
                return null;
            }), null);
        Dispatcher.PushFrame(frame);
    }

    // ---------------- 托盘区域截图验证 ----------------

    /// <summary>
    /// 截取任务栏托盘区域并统计「应用徽标配色」像素占比，用于验证托盘图标是否真的显示出来
    /// （Shell_NotifyIcon 返回成功不代表图标一定可见，可能被 Windows 收进隐藏图标区）。
    /// </summary>
    private async void RunTrayShot()
    {
        try
        {
            const int width = 420;
            await Task.Delay(2500); // 等托盘项完成注册与显示

            var workArea = SystemParameters.WorkArea;
            int x = (int)(workArea.Right - width);
            int y = (int)workArea.Bottom;
            int height = (int)(SystemParameters.PrimaryScreenHeight - workArea.Bottom);

            var hdcScreen = Win32.GetDC(IntPtr.Zero);
            try
            {
                var hdcMem = Win32.CreateCompatibleDC(hdcScreen);
                var hBmp = Win32.CreateCompatibleBitmap(hdcScreen, width, height);
                var old = Win32.SelectObject(hdcMem, hBmp);
                Win32.BitBlt(hdcMem, 0, 0, width, height, hdcScreen, x, y, Win32.SRCCOPY);
                Win32.SelectObject(hdcMem, old);

                var source = Imaging.CreateBitmapSourceFromHBitmap(
                    hBmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();

                Win32.DeleteObject(hBmp);
                Win32.DeleteDC(hdcMem);

                SaveRawPng(source, "docs/screenshots/tray-area.png");

                // 徽标为蓝紫渐变（#0A84FF -> #5E5CE6），统计此类像素数量
                int hits = CountAccentPixels(source);
                Directory.CreateDirectory("docs/perf");
                File.WriteAllText(Path.Combine("docs/perf", "tray-check.txt"),
                    $"region={width}x{height} at ({x},{y}){Environment.NewLine}" +
                    $"accent_pixel_hits={hits}{Environment.NewLine}" +
                    (hits > 20 ? "RESULT=图标已显示在托盘区域" : "RESULT=未命中，图标可能位于隐藏图标区或未显示") +
                    Environment.NewLine);
            }
            finally
            {
                Win32.ReleaseDC(IntPtr.Zero, hdcScreen);
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine("托盘截图失败: " + ex);
        }

        Cleanup();
        Shutdown(0);
    }

    /// <summary>统计接近应用徽标配色的像素数（蓝色加权，排除常见桌面/任务栏灰）。</summary>
    private static int CountAccentPixels(BitmapSource source)
    {
        int w = source.PixelWidth;
        int h = source.PixelHeight;
        int hits = 0;

        for (int y = 0; y < h; y++)
        {
            var row = new byte[w * 4];
            var crop = new CroppedBitmap(source, new Int32Rect(0, y, w, 1));
            crop.CopyPixels(row, w * 4, 0);

            for (int i = 0; i < row.Length; i += 4)
            {
                byte b = row[i], g = row[i + 1], r = row[i + 2];
                // 蓝色显著高于红绿，且不是纯深蓝背景
                if (b > 140 && b - r > 60 && b - g > 40) hits++;
            }
        }

        return hits;
    }

    // ---------------- 拖动行为验证 ----------------

    /// <summary>
    /// 用真实鼠标输入验证「整条标题栏都能拖动」：
    /// 在标题栏中段（既不是 logo 也不是按钮）按下并拖到偏移处，检查窗口是否随之移动。
    /// 结果写入 docs/perf/drag-check.txt。
    /// </summary>
    private async void RunDragTest()
    {
        var lines = new StringBuilder();
        try
        {
            ApplyTheme(_isDark);
            ShowMainWindow();
            await Task.Delay(1400);

            if (_mainWindow is null) throw new InvalidOperationException("主窗口为空");

            var hwnd = new WindowInteropHelper(_mainWindow).Handle;
            Win32.GetWindowRect(hwnd, out var before);

            // 取标题栏横向中点：避开左侧 logo 与右侧按钮，最能代表“空白处是否可拖”
            int startX = before.Left + (before.Right - before.Left) / 2;
            int startY = before.Top + 26;
            const int expectDx = 90;
            const int expectDy = 60;

            lines.AppendLine($"before=({before.Left},{before.Top}) size={before.Right - before.Left}x{before.Bottom - before.Top}");
            lines.AppendLine($"grab_point=({startX},{startY}) expected_delta=({expectDx},{expectDy})");

            await DragMouse(startX, startY, expectDx, expectDy);
            await Task.Delay(500);

            Win32.GetWindowRect(hwnd, out var after);
            int dx = after.Left - before.Left;
            int dy = after.Top - before.Top;

            lines.AppendLine($"after=({after.Left},{after.Top}) actual_delta=({dx},{dy})");
            bool ok = Math.Abs(dx - expectDx) <= 6 && Math.Abs(dy - expectDy) <= 6;
            lines.AppendLine(ok
                ? "RESULT=标题栏空白处可拖动，位移符合预期"
                : "RESULT=拖动无效或位移不符，需要检查拖动手势绑定");

            // 第二段：在“关闭”按钮上按下不放，窗口不应被拖走（按钮不应触发拖动）
            Win32.GetWindowRect(hwnd, out var beforeBtn);
            int btnX = beforeBtn.Right - 26;
            int btnY = beforeBtn.Top + 26;
            lines.AppendLine($"button_point=({btnX},{btnY})");

            await DragMouse(btnX, btnY, 60, 40);
            await Task.Delay(400);

            bool visible = _mainWindow.IsVisible;
            Win32.GetWindowRect(hwnd, out var afterBtn);
            int bdx = afterBtn.Left - beforeBtn.Left;
            int bdy = afterBtn.Top - beforeBtn.Top;
            lines.AppendLine($"button_drag_delta=({bdx},{bdy}) still_visible={visible}");
            lines.AppendLine((bdx == 0 && bdy == 0)
                ? "RESULT2=按钮上按下不会误拖动（已排除按钮区域）"
                : "RESULT2=按钮区域仍被当作拖动手势，需要修复");
        }
        catch (Exception ex)
        {
            lines.AppendLine("drag_test_failed=" + ex.Message);
        }

        Directory.CreateDirectory("docs/perf");
        File.WriteAllText(Path.Combine("docs/perf", "drag-check.txt"), lines.ToString());
        Trace.WriteLine(lines.ToString());

        Cleanup();
        Shutdown(0);
    }

    /// <summary>用 SendInput 模拟「按下 -> 分步移动 -> 松开」的真实拖动。</summary>
    private static async Task DragMouse(int x, int y, int dx, int dy)
    {
        double vw = SystemParameters.PrimaryScreenWidth;
        double vh = SystemParameters.PrimaryScreenHeight;

        void Move(int px, int py)
        {
            int ax = (int)Math.Clamp(px * 65535.0 / vw, 0, 65535);
            int ay = (int)Math.Clamp(py * 65535.0 / vh, 0, 65535);
            SendMouse(Win32.MOUSEEVENTF_MOVE | Win32.MOUSEEVENTF_ABSOLUTE, ax, ay);
        }

        Move(x, y);
        await Task.Delay(100);
        SendMouse(Win32.MOUSEEVENTF_LEFTDOWN, 0, 0);
        await Task.Delay(100);

        for (int i = 1; i <= 6; i++)
        {
            Move(x + dx * i / 6, y + dy * i / 6);
            await Task.Delay(50);
        }

        SendMouse(Win32.MOUSEEVENTF_LEFTUP, 0, 0);
        await Task.Delay(150);
    }

    private static void SendMouse(uint flags, int dx, int dy)
    {
        var input = new Win32.INPUT
        {
            type = Win32.INPUT_MOUSE,
            u = new Win32.INPUTUNION
            {
                mi = new Win32.MOUSEINPUT
                {
                    dx = dx,
                    dy = dy,
                    mouseData = 0,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        Win32.SendInput(1, new[] { input }, Marshal.SizeOf<Win32.INPUT>());
    }

    /// <summary>把窗口内容合成为 PNG（底下垫一条桌面渐变，模拟亚克力背后的内容）。</summary>
    private static void SavePng(FrameworkElement content, string path, int width, int height, bool dark)
    {
        var foreground = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        foreground.Render(content);

        var backdrop = dark
            ? new LinearGradientBrush(Color.FromRgb(20, 20, 22), Color.FromRgb(46, 46, 52), 90)
            : new LinearGradientBrush(Color.FromRgb(214, 219, 226), Color.FromRgb(236, 239, 244), 90);

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(backdrop, null, new Rect(0, 0, width, height));
            dc.DrawImage(foreground, new Rect(0, 0, width, height));
        }

        var final = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        final.Render(dv);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(final));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var fs = File.Create(path);
        encoder.Save(fs);
    }

    // ---------------- 性能实测模式 ----------------

    /// <summary>
    /// 性能实测：后台空闲内存 / 面板唤出耗时 / 连续开关 100 次后的内存变化。
    /// 所有数字来自真实采样，不做估算。结果写入 docs/perf/benchmark.md。
    /// </summary>
    private async void RunBenchmark()
    {
        var report = new StringBuilder();
        try
        {
            var window = _mainWindow!;

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var idle = SnapshotMemory();
            report.AppendLine($"idle_private_kb={idle.PrivateKb:0.0}");
            report.AppendLine($"idle_workingset_kb={idle.WorkingSetKb:0.0}");

            var times = new List<double>(100);
            for (int i = 1; i <= 100; i++)
            {
                var sw = Stopwatch.StartNew();
                window.FocusSearch();
                window.Show();
                // 等待真正完成一帧合成，才算「可见」
                await WaitNextRenderAsync();
                sw.Stop();
                times.Add(sw.Elapsed.TotalMilliseconds);

                window.Hide();
                await WaitNextRenderAsync();

                // 每 20 次采样一次内存，用于区分「JIT/缓存预热」与「真实泄漏」：
                // 预热型增长会很快趋于平稳；泄漏则会持续线性上涨。
                if (i % 20 == 0)
                {
                    // 先做完整回收再采样：能被回收的部分属于缓存/预热，剩下的才是疑似泄漏
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    var s = SnapshotMemory();
                    report.AppendLine($"cycle_{i}_private_kb={s.PrivateKb:0.0}");
                    report.AppendLine($"cycle_{i}_workingset_kb={s.WorkingSetKb:0.0}");
                }
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var after = SnapshotMemory();
            var sorted = times.OrderBy(t => t).ToList();

            report.AppendLine($"cycles={times.Count}");
            report.AppendLine($"show_avg_ms={times.Average():0.00}");
            report.AppendLine($"show_min_ms={times.Min():0.00}");
            report.AppendLine($"show_p95_ms={sorted[(int)(0.95 * sorted.Count)]:0.00}");
            report.AppendLine($"show_max_ms={times.Max():0.00}");
            report.AppendLine($"after_private_kb={after.PrivateKb:0.0}");
            report.AppendLine($"after_workingset_kb={after.WorkingSetKb:0.0}");
            report.AppendLine($"private_delta_kb={after.PrivateKb - idle.PrivateKb:0.0}");
            report.AppendLine($"workingset_delta_kb={after.WorkingSetKb - idle.WorkingSetKb:0.0}");
        }
        catch (Exception ex)
        {
            report.AppendLine("benchmark_failed=" + ex.Message);
        }

        Directory.CreateDirectory("docs/perf");
        File.WriteAllText(Path.Combine("docs/perf", "benchmark.txt"), report.ToString());
        Trace.WriteLine(report.ToString());

        Cleanup();
        Shutdown(0);
    }

    private static Task WaitNextRenderAsync()
    {
        var tcs = new TaskCompletionSource();
        void Handler(object? sender, EventArgs e)
        {
            CompositionTarget.Rendering -= Handler;
            tcs.TrySetResult();
        }

        CompositionTarget.Rendering += Handler;
        return tcs.Task;
    }

    /// <summary>采样当前进程的内存占用（私有提交页数 + 工作集）。</summary>
    private static (double PrivateKb, double WorkingSetKb) SnapshotMemory()
    {
        using var process = Process.GetCurrentProcess();
        if (Win32.GetProcessMemoryInfo(process.Handle, out var counters,
                (uint)Marshal.SizeOf<Win32.PROCESS_MEMORY_COUNTERS>()))
        {
            return ((double)counters.PrivateUsage / 1024.0, (double)counters.WorkingSetSize / 1024.0);
        }

        return (process.PrivateMemorySize64 / 1024.0, process.WorkingSet64 / 1024.0);
    }

    // ---------------- 真实屏幕捕获（验证 DWM 圆角与黑边） ----------------

    /// <summary>
    /// 把真实窗口连同桌面一起截下来（BitBlt 屏幕 DC），而不是离屏渲染。
    /// 只有真实合成结果才能验证：DWM 原生圆角、外层是否残留黑色矩形、亚克力是否生效。
    /// 同时对四角做数值检测，输出到 docs/perf/corner-check.txt。
    /// </summary>
    private async void RunRealCapture()
    {
        try
        {
            // 强制主题顺序，不依赖本地设置文件（避免上一次运行残留的偏好导致文件名与内容不符）
            SetThemeExplicit(true);
            ShowMainWindow();
            await Task.Delay(1600); // 等动画、亚克力与首帧合成稳定

            await CaptureRealWindow("docs/screenshots/real-desktop-dark.png");

            SetThemeExplicit(false);
            await Task.Delay(600);
            await CaptureRealWindow("docs/screenshots/real-desktop-light.png");
        }
        catch (Exception ex)
        {
            Trace.WriteLine("真实捕获失败: " + ex);
        }

        Cleanup();
        Shutdown(0);
    }

    /// <summary>显式设定主题并刷新可见窗口的 DWM 外观（不经由用户设置持久化）。</summary>
    private void SetThemeExplicit(bool dark)
    {
        _isDark = dark;
        ApplyTheme(_isDark);
        if (_mainWindow is { IsVisible: true }) ApplyWindowChrome(_mainWindow);
    }

    private async Task CaptureRealWindow(string path)
    {
        if (_mainWindow is null) return;
        var hwnd = new WindowInteropHelper(_mainWindow).Handle;
        if (!Win32.GetWindowRect(hwnd, out var rect)) return;

        // 把鼠标移开窗口，避免 ToolTip / Hover 态混入验收截图
        Win32.SetCursorPos(2, 2);
        await Task.Delay(300);

        int w = rect.Right - rect.Left;
        int h = rect.Bottom - rect.Top;
        if (w <= 0 || h <= 0) return;

        var hdcScreen = Win32.GetDC(IntPtr.Zero);
        try
        {
            var hdcMem = Win32.CreateCompatibleDC(hdcScreen);
            var hBmp = Win32.CreateCompatibleBitmap(hdcScreen, w, h);
            var old = Win32.SelectObject(hdcMem, hBmp);
            Win32.BitBlt(hdcMem, 0, 0, w, h, hdcScreen, rect.Left, rect.Top, Win32.SRCCOPY);
            Win32.SelectObject(hdcMem, old);

            var source = Imaging.CreateBitmapSourceFromHBitmap(
                hBmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();

            Win32.DeleteObject(hBmp);
            Win32.DeleteDC(hdcMem);

            SaveRawPng(source, path);

            // 四角黑边检测：统计每个 14×14 角部区域内接近纯黑的像素占比
            CheckCorners(source, path);
        }
        finally
        {
            Win32.ReleaseDC(IntPtr.Zero, hdcScreen);
        }
    }

    private static void SaveRawPng(BitmapSource source, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var fs = File.Create(path);
        encoder.Save(fs);
    }

    /// <summary>角部近黑像素占比检测。圆角正常时，角外露出的是桌面壁纸，不应是纯黑矩形。</summary>
    private static void CheckCorners(BitmapSource source, string path)
    {
        int size = 14;
        int w = source.PixelWidth;
        int h = source.PixelHeight;
        var report = new StringBuilder();
        report.AppendLine($"file={path} size={w}x{h} ( dpiScaleX={source.DpiX / 96.0:0.##} )");

        foreach (var (name, x0, y0) in new[]
                 {
                     ("top-left", 0, 0),
                     ("top-right", w - size, 0),
                     ("bottom-left", 0, h - size),
                     ("bottom-right", w - size, h - size)
                 })
        {
            var stride = size * 4;
            var pixels = new byte[stride * size];
            var crop = new CroppedBitmap(source, new Int32Rect(Math.Max(0, x0), Math.Max(0, y0), size, size));
            crop.CopyPixels(pixels, stride, 0);

            int black = 0;
            for (int i = 0; i < pixels.Length; i += 4)
            {
                // BGR；接近纯黑（且不透明）视为疑似黑边残留
                if (pixels[i] < 8 && pixels[i + 1] < 8 && pixels[i + 2] < 8 && pixels[i + 3] > 200)
                    black++;
            }

            report.AppendLine($"  {name}: near_black_ratio={(double)black / (size * size):0.00}");
        }

        Directory.CreateDirectory("docs/perf");
        File.AppendAllText(Path.Combine("docs/perf", "corner-check.txt"), report.ToString() + Environment.NewLine);
    }

    private static bool HasArg(string[] args, string target)
        => args is not null && Array.Exists(args, a => a.Equals(target, StringComparison.OrdinalIgnoreCase));
}
