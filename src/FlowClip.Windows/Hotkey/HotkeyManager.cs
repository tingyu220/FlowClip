using System;
using FlowClip.Windows.Native;

namespace FlowClip.Windows.Hotkey;

/// <summary>
/// 全局快捷键修饰键（对应 Win32 MOD_* 常量）。
/// </summary>
[Flags]
public enum HotkeyModifiers
{
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Win = 0x0008,
    NoRepeat = 0x4000,
}

/// <summary>
/// 基于 RegisterHotKey 的全局快捷键管理。
/// 通过共享的消息窗口接收 WM_HOTKEY，触发 HotkeyPressed 事件。
/// 注意：仅注册 Ctrl+Shift+* 之类组合，绝不占用 Ctrl+C / Ctrl+V，避免破坏系统复制粘贴。
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private readonly MessageWindow _window;
    private int _id = 1;
    private bool _registered;
    private bool _disposed;

    public event EventHandler? HotkeyPressed;

    public HotkeyManager(MessageWindow window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _window.MessageReceived += OnMessage;
    }

    /// <summary>
    /// 注册全局快捷键。modifiers 为修饰键组合，virtualKey 为虚拟键码（如 'V' = 0x56）。
    /// 返回是否注册成功。
    /// </summary>
    public bool Register(HotkeyModifiers modifiers, uint virtualKey)
    {
        if (_registered)
        {
            Unregister();
        }

        _registered = Win32.RegisterHotKey(_window.Handle, _id, (uint)modifiers, virtualKey);
        return _registered;
    }

    public void Unregister()
    {
        if (_registered)
        {
            Win32.UnregisterHotKey(_window.Handle, _id);
            _registered = false;
        }
    }

    private void OnMessage(object? sender, Win32.WindowMessage e)
    {
        if (e.Msg == Win32.WM_HOTKEY && e.WParam.ToInt32() == _id)
        {
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
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
        Unregister();
    }
}
