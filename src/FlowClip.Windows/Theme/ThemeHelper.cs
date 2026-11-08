using System;
using Microsoft.Win32;
using FlowClip.Windows.Native;

namespace FlowClip.Windows.Theme;

/// <summary>
/// 主题与 DWM 辅助：检测系统深浅色、设置窗口沉浸式暗色模式、按主题返回亚克力着色。
/// </summary>
public static class ThemeHelper
{
    // Win10 20H1+ / Win11 通用值
    private const uint DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    // Win11 圆角偏好（DWMWA_WINDOW_CORNER_PREFERENCE）
    private const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_DEFAULT = 0;
    private const int DWMWCP_ROUND = 2;

    // Win11 窗口边框颜色（DWMWA_BORDER_COLOR）；COLOR_NONE 表示完全去除 DWM 绘制的边框
    private const uint DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);
    /// <summary>
    /// 读取系统是否处于暗色模式（Personalize -> AppsUseLightTheme，0 表示暗色）。
    /// 读取失败或不可用环境默认返回 false（浅色）。
    /// </summary>
    public static bool IsSystemDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", false);
            if (key?.GetValue("AppsUseLightTheme") is int value)
            {
                return value == 0;
            }
        }
        catch (Exception)
        {
            // 忽略，返回默认
        }

        return false;
    }

    /// <summary>
    /// 设置窗口沉浸式暗色模式（影响标题栏/边框等系统绘制部分）。
    /// </summary>
    public static void SetImmersiveDarkMode(IntPtr hwnd, bool dark)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        int value = dark ? 1 : 0;
        Win32.DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
    }

    /// <summary>
    /// 返回对应主题的亚克力着色（0xAARRGGBB）。浅色偏白、暗色偏深。
    /// </summary>
    public static uint AcrylicTint(bool dark)
    {
        // alpha 约 0xB0（176）：足够透出模糊，又能保证文字对比
        return dark ? 0xB01C1C1E : 0xB0F2F2F7;
    }

    /// <summary>
    /// 处理 DWM 在圆角外沿绘制的 1px 系统边框。
    /// 深色模式下该边框默认呈纯黑色，会在圆角弧线上留下细黑圈，与玻璃质感冲突。
    /// 优先尝试 COLOR_NONE（完全去边框）；系统不支持时回退为「与面板同色」的隐性边框。
    /// </summary>
    public static void ApplyBorderColor(IntPtr hwnd, bool dark)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var none = DWMWA_COLOR_NONE;
            int hr = Win32.DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref none, sizeof(int));
            if (hr == 0)
            {
                return;
            }

            // 回退：把边框染成与面板一致的颜色，视觉上隐形
            var panel = dark ? unchecked((int)0xFF1C1C1E) : unchecked((int)0xFFF2F2F7);
            Win32.DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref panel, sizeof(int));
        }
        catch (Exception)
        {
            // 忽略：旧系统无该属性
        }
    }

    /// <summary>
    /// 设置窗口圆角偏好。round=true 时让 DWM 原生绘制圆角（消除自定义窗口的直角黑边）。
    /// 失败（如旧系统无该属性）静默忽略。
    /// </summary>
    public static void SetRoundedCorners(IntPtr hwnd, bool round)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        int pref = round ? DWMWCP_ROUND : DWMWCP_DEFAULT;
        try
        {
            Win32.DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
        }
        catch (Exception)
        {
            // 忽略
        }
    }
}
