using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using FlowClip.Windows.Native;

namespace FlowClip.Windows.Tray;

/// <summary>
/// 系统托盘图标管理（基于 Shell_NotifyIcon + 共享消息窗口回调）。
/// 提供左键打开、右键菜单的事件，由 App 层用 WPF 上下文菜单呈现。
/// </summary>
public sealed class TrayManager : IDisposable
{
    private readonly MessageWindow _window;
    private readonly uint _callbackMsg = Win32.WM_USER + 1;
    private Win32.NOTIFYICONDATA _data;
    private IntPtr _icon;
    private string? _tempIconFile;
    private bool _added;
    private bool _disposed;

    /// <summary>左键（单击 / 双击）触发：打开主面板</summary>
    public event EventHandler? OpenRequested;

    /// <summary>右键触发：显示上下文菜单</summary>
    public event EventHandler? ContextMenuRequested;

    public TrayManager(MessageWindow window, string tooltip)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _icon = LoadIconFromEmbedded();
        _data = new Win32.NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<Win32.NOTIFYICONDATA>(),
            hWnd = _window.Handle,
            uID = 1,
            uFlags = Win32.NIF_MESSAGE | Win32.NIF_ICON | Win32.NIF_TIP,
            uCallbackMessage = _callbackMsg,
            hIcon = _icon,
            szTip = tooltip.Length > 127 ? tooltip.Substring(0, 127) : tooltip,
        };

        _added = Win32.Shell_NotifyIcon(Win32.NIM_ADD, ref _data);
        Log($"tray_add_ok={_added} icon=0x{_icon.ToInt64():X} cbSize={_data.cbSize}");

        if (_icon == IntPtr.Zero)
        {
            // 图标为空时 Shell 不会显示托盘项，这里是最高优先级问题：立即在日志里暴露
            Log("ERROR: 托盘图标句柄为空，请检查嵌入资源 Assets/icon.ico");
        }

        // 启用气泡提示所需的版本协商
        var versionData = _data;
        versionData.uVersion = 4;
        Win32.Shell_NotifyIcon(Win32.NIM_SETVERSION, ref versionData);

        _window.MessageReceived += OnMessage;
    }

    private void OnMessage(object? sender, Win32.WindowMessage e)
    {
        if (e.Msg != _callbackMsg)
        {
            return;
        }

        // lParam 的低字为鼠标消息
        var mouse = (uint)(e.LParam.ToInt32() & 0xFFFF);
        switch (mouse)
        {
            case 0x0202: // WM_LBUTTONUP
            case 0x0203: // WM_LBUTTONDBLCLK
                OpenRequested?.Invoke(this, EventArgs.Empty);
                break;
            case 0x0205: // WM_RBUTTONUP
                ContextMenuRequested?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    /// <summary>
    /// 显示托盘气泡提示。
    /// 用于「无法自动粘贴，请手动 Ctrl+V」这类降级提示：不抢占焦点、不打断当前操作。
    /// </summary>
    public void ShowBalloon(string title, string text)
    {
        if (_disposed || !_added) return;

        var data = _data;
        data.uFlags |= Win32.NIF_INFO;
        data.dwInfoFlags = Win32.NIIF_INFO;
        data.szInfoTitle = Trim(title, 63);
        data.szInfo = Trim(text, 255);

        // 注意：这里不能用 ref _data 的副本以外的引用去修改结构体字段本身，
        // 因此使用局部变量传入，避免影响托盘图标的既有状态。
        Win32.Shell_NotifyIcon(Win32.NIM_MODIFY, ref data);
    }

    private static string Trim(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max ? s ?? string.Empty : s.Substring(0, max);

    /// <summary>
    /// 从嵌入资源提取 .ico 到临时文件，再取回 HICON。
    ///
    /// 取尺寸的注意事项：LoadImage 指定 cx/cy=0 时会试图加载最大的 256×256 条目
    /// （PNG 压缩），部分系统上会失败并返回 0，导致托盘项不显示。
    /// 因此先按 32×32 取；失败则用 ExtractIconEx 取 16×16 兜底。
    /// </summary>
    private IntPtr LoadIconFromEmbedded()
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = Array.Find(asm.GetManifestResourceNames(),
            n => n.EndsWith("icon.ico", StringComparison.OrdinalIgnoreCase));
        Log($"resource_found={name is not null} names=[{string.Join(",", asm.GetManifestResourceNames())}]");
        if (name is null)
        {
            return IntPtr.Zero;
        }

        var tmp = Path.Combine(Path.GetTempPath(), "flowclip_icon_" + Guid.NewGuid().ToString("N") + ".ico");
        using var stream = asm.GetManifestResourceStream(name);
        if (stream is null)
        {
            Log("ERROR: 无法打开嵌入资源流");
            return IntPtr.Zero;
        }

        using (var fs = File.Create(tmp))
        {
            stream.CopyTo(fs);
        }

        _tempIconFile = tmp;
        Log($"temp_icon_size={new FileInfo(tmp).Length}");

        // 1) 明确按 32×32 取
        var hIcon = Win32.LoadImage(IntPtr.Zero, tmp, Win32.IMAGE_ICON, 32, 32, Win32.LR_LOADFROMFILE);
        if (hIcon != IntPtr.Zero)
        {
            Log("LoadImage(32x32) ok");
            return hIcon;
        }

        // 2) 兜底：ExtractIconEx 取 16×16
        var small = new IntPtr[1];
        if (Win32.ExtractIconEx(tmp, 0, null, small, 1) > 0 && small[0] != IntPtr.Zero)
        {
            Log("ExtractIconEx(16x16) ok");
            return small[0];
        }

        Log("ERROR: 图标加载全部失败");
        return IntPtr.Zero;
    }

    /// <summary>把托盘相关诊断写入本地日志，便于不在 Visual Studio 下排查。</summary>
    private static void Log(string message)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FlowClip", "logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "tray.log"),
                $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // 日志写入失败不影响主流程
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _window.MessageReceived -= OnMessage;
        if (_added)
        {
            Win32.Shell_NotifyIcon(Win32.NIM_DELETE, ref _data);
        }

        if (_icon != IntPtr.Zero)
        {
            Win32.DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }

        if (_tempIconFile is not null && File.Exists(_tempIconFile))
        {
            try { File.Delete(_tempIconFile); } catch { /* 忽略 */ }
        }
    }
}
