using System;
using System.Runtime.InteropServices;
using FlowClip.Windows.Native;

namespace FlowClip.Windows.Native;

/// <summary>
/// 消息-only 窗口（HWND_MESSAGE）。用于接收全局热键、剪贴板更新与托盘回调。
/// 必须在 UI 线程（WPF Dispatcher 所在线程）创建，以便由 Dispatcher 的消息泵分发窗口消息。
/// </summary>
public sealed class MessageWindow : IDisposable
{
    private readonly string _className;
    private IntPtr _hwnd = IntPtr.Zero;
    private readonly Win32.WndProc _wndProc;
    private GCHandle _wndProcHandle;
    private bool _disposed;

    public IntPtr Handle => _hwnd;

    public event EventHandler<Win32.WindowMessage>? MessageReceived;

    public MessageWindow()
    {
        _className = "FlowClipMessageWindow_" + Guid.NewGuid().ToString("N");
        _wndProc = WndProcThunk;
        _wndProcHandle = GCHandle.Alloc(_wndProc);

        var wc = new Win32.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<Win32.WNDCLASSEX>(),
            style = 0,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = Win32.GetModuleHandle(null),
            lpszClassName = _className,
        };

        if (Win32.RegisterClassEx(ref wc) == 0)
        {
            throw new InvalidOperationException("注册消息窗口类失败: " + Marshal.GetLastWin32Error());
        }

        // 父窗口使用 HWND_MESSAGE(-3)，使其成为仅消息窗口，不显示在任务栏/桌面
        _hwnd = Win32.CreateWindowEx(
            0,
            _className,
            "FlowClipMessageWindow",
            0,
            0, 0, 0, 0,
            new IntPtr(-3),
            IntPtr.Zero,
            Win32.GetModuleHandle(null),
            IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException("创建消息窗口失败: " + Marshal.GetLastWin32Error());
        }
    }

    private IntPtr WndProcThunk(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        MessageReceived?.Invoke(this, new Win32.WindowMessage(hWnd, msg, wParam, lParam));
        if (msg == Win32.WM_DESTROY)
        {
            return IntPtr.Zero;
        }

        return Win32.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_hwnd != IntPtr.Zero)
        {
            Win32.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        if (_wndProcHandle.IsAllocated)
        {
            _wndProcHandle.Free();
        }
    }
}
