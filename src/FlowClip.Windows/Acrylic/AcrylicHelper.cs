using System;
using System.Runtime.InteropServices;
using FlowClip.Windows.Native;

namespace FlowClip.Windows.Acrylic;

/// <summary>
/// 亚克力（Acrylic）材质辅助：将 DWM 玻璃框扩展到客户区并启用亚克力模糊。
/// 若系统不支持（SetWindowCompositionAttribute 返回 0），调用方应回退到半透明纯色面板。
/// </summary>
public static class AcrylicHelper
{
    /// <summary>
    /// 为指定窗口启用亚克力。tintArgb 为 0xAARRGGBB 着色（含不透明度），alpha 越大越不透明。
    /// </summary>
    /// <returns>true 表示亚克力启用成功；false 表示系统不支持，应回退。</returns>
    public static bool EnableAcrylic(IntPtr hwnd, uint tintArgb)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        // 1) 将玻璃框扩展到整个客户区，使亚克力覆盖窗口内容区域
        var margins = new Win32.MARGINS
        {
            cxLeftWidth = -1,
            cxRightWidth = -1,
            cyTopHeight = -1,
            cyBottomHeight = -1,
        };
        Win32.DwmExtendFrameIntoClientArea(hwnd, ref margins);

        // 2) 通过 WCA_ACCENT_POLICY 启用亚克力模糊
        // AccentFlags 必须声明绘制四边（0x20/0x40/0x80/0x100），
        // 否则模糊不会铺满边框区域，DWM 玻璃帧会在圆角外沿露出 3-4px 纯黑边。
        const uint ACCENT_FLAG_DRAW_LEFT = 0x20;
        const uint ACCENT_FLAG_DRAW_TOP = 0x40;
        const uint ACCENT_FLAG_DRAW_RIGHT = 0x80;
        const uint ACCENT_FLAG_DRAW_BOTTOM = 0x100;

        var accent = new Win32.ACCENT_POLICY
        {
            AccentState = Win32.AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND,
            AccentFlags = (int)(ACCENT_FLAG_DRAW_LEFT | ACCENT_FLAG_DRAW_TOP
                              | ACCENT_FLAG_DRAW_RIGHT | ACCENT_FLAG_DRAW_BOTTOM),
            GradientColor = tintArgb,
            AnimationId = 0,
        };

        var data = new Win32.WINDOWCOMPOSITIONATTRIBDATA
        {
            Attrib = Win32.WCA_ACCENT_POLICY,
            pvData = Marshal.AllocHGlobal(Marshal.SizeOf<Win32.ACCENT_POLICY>()),
            cbData = Marshal.SizeOf<Win32.ACCENT_POLICY>(),
        };

        try
        {
            Marshal.StructureToPtr(accent, data.pvData, false);
            var result = Win32.SetWindowCompositionAttribute(hwnd, ref data);
            return result != 0;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(data.pvData);
        }
    }

    /// <summary>
    /// 关闭亚克力（恢复为普通窗口）。用于回退场景。
    /// </summary>
    public static void DisableAcrylic(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var accent = new Win32.ACCENT_POLICY
        {
            AccentState = Win32.AccentState.ACCENT_DISABLED,
            AccentFlags = 0,
            GradientColor = 0,
            AnimationId = 0,
        };

        var data = new Win32.WINDOWCOMPOSITIONATTRIBDATA
        {
            Attrib = Win32.WCA_ACCENT_POLICY,
            pvData = Marshal.AllocHGlobal(Marshal.SizeOf<Win32.ACCENT_POLICY>()),
            cbData = Marshal.SizeOf<Win32.ACCENT_POLICY>(),
        };

        try
        {
            Marshal.StructureToPtr(accent, data.pvData, false);
            Win32.SetWindowCompositionAttribute(hwnd, ref data);
        }
        catch (Exception)
        {
            // 忽略
        }
        finally
        {
            Marshal.FreeHGlobal(data.pvData);
        }
    }
}
