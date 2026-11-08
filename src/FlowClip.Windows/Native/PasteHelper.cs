using System;

namespace FlowClip.Windows.Native;

/// <summary>
/// 向目标窗口投递 Ctrl+V。
///
/// 之所以需要它：FlowClip 本身没有目标窗口的编程接口（Word / 浏览器输入框 / 资源管理器等），
/// 只有在「目标窗口确定存在且能够安全激活」时才替用户完成粘贴；
/// 一旦条件不满足，必须降级为「只复制到剪贴板」并明确提示，
/// 绝不允许把内容误输入到别的窗口里。
/// </summary>
public static class PasteHelper
{
    private const ushort VkControl = 0x11;
    private const ushort VkV = 0x56;

    /// <summary>
    /// 尝试激活目标窗口并发送 Ctrl+V。返回是否成功。
    /// </summary>
    public static bool TrySendCtrlV(IntPtr target)
    {
        if (target == IntPtr.Zero) return false;

        // 目标窗口必须仍然存在且可交互
        if (!Win32.IsWindowVisible(target) || !Win32.IsWindowEnabled(target)) return false;

        uint currentThread = Win32.GetCurrentThreadId();
        uint targetThread = Win32.GetWindowThreadProcessId(target, out _);

        // 与目标窗口所属线程临时共享输入队列，提升跨进程 SetForegroundWindow 的成功率
        bool attached = targetThread != currentThread &&
                        Win32.AttachThreadInput(currentThread, targetThread, true);
        try
        {
            if (!Win32.SetForegroundWindow(target)) return false;
            SendCtrlV();
            return true;
        }
        finally
        {
            if (attached) Win32.AttachThreadInput(currentThread, targetThread, false);
        }
    }

    private static void SendCtrlV()
    {
        var inputs = new Win32.INPUT[4];
        int size = System.Runtime.InteropServices.Marshal.SizeOf<Win32.INPUT>();

        inputs[0] = KeyInput(VkControl, Win32.KEYEVENTF_KEYDOWN);
        inputs[1] = KeyInput(VkV, Win32.KEYEVENTF_KEYDOWN);
        inputs[2] = KeyInput(VkV, Win32.KEYEVENTF_KEYUP);
        inputs[3] = KeyInput(VkControl, Win32.KEYEVENTF_KEYUP);

        Win32.SendInput((uint)inputs.Length, inputs, size);
    }

    private static Win32.INPUT KeyInput(ushort vk, uint flags)
        => new()
        {
            type = Win32.INPUT_KEYBOARD,
            u = new Win32.INPUTUNION
            {
                ki = new Win32.KEYBDINPUT
                {
                    wVk = vk,
                    wScan = 0,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
}
