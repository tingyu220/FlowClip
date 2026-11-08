using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace FlowClip.App.Infrastructure;

/// <summary>
/// 获取文件类型的系统图标（按扩展名 / 目录），不读取文件内容，避免把大文件读进内存。
///
/// 两级策略：
/// 1. SHGetFileInfo + SHGFI_USEFILEATTRIBUTES：最标准的方式，仅凭扩展名向 Shell 取图标；
/// 2. 注册表兜底：HKCR\扩展名 → ProgID → DefaultIcon → PrivateExtractIcons。
///    某些受限会话（服务进程、部分沙箱）里 SHGetFileInfo 会失败，注册表路径不依赖 Shell 消息循环，
///    可以稳定拿到「该扩展名当前真实关联的应用图标」。
///
/// 结果按扩展名缓存，重复卡片不会重复查询。
/// </summary>
public static class ShellIconHelper
{
    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_LARGEICON = 0x0;        // 32×32 大图标（卡片 42px 徽标位足够清晰）
    private const uint SHGFI_USEFILEATTRIBUTES = 0x800;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct ShFileInfo
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath, uint dwFileAttributes, ref ShFileInfo psfi, uint cbSize, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint PrivateExtractIcons(
        string szFileName, int nIconIndex, int cxIcon, int cyIcon,
        IntPtr[]? phicon, int[]? piconid, int nIcons, int flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Lock = new();

    /// <summary>
    /// 取指定扩展名（"pdf" 或 "报告.pdf"）对应的系统图标；isDirectory=true 时返回文件夹图标。
    /// 返回 null 表示无法获取（调用方应回退到文字徽标）。
    /// </summary>
    public static ImageSource? GetIconForExtension(string? extensionOrName, bool isDirectory)
    {
        var key = isDirectory ? "___dir" : NormalizeKey(extensionOrName);
        lock (Lock)
        {
            if (Cache.TryGetValue(key, out var cached)) return cached;
        }

        var result = isDirectory
            ? GetFolderIcon()
            : GetFileIcon(key);

        lock (Lock)
        {
            Cache[key] = result;
        }

        return result;
    }

    /// <summary>清空图标缓存（极少需要；留作主题/关联变更时的保险）。</summary>
    public static void ClearCache()
    {
        lock (Lock) Cache.Clear();
    }

    // ---------------- 策略一：SHGetFileInfo ----------------

    private static ImageSource? GetFileIcon(string normalizedExt)
    {
        try
        {
            var probe = string.IsNullOrEmpty(normalizedExt) ? "file" : ("file." + normalizedExt);
            var info = new ShFileInfo();
            // SHGFI_USEFILEATTRIBUTES：只依据属性与扩展名取图标，
            // 不会打开或读取真实文件内容，因此不存在把大文件读进内存的风险。
            var flags = SHGFI_ICON | SHGFI_LARGEICON | SHGFI_USEFILEATTRIBUTES;
            SHGetFileInfo(probe, FILE_ATTRIBUTE_NORMAL, ref info,
                (uint)Marshal.SizeOf<ShFileInfo>(), flags);
            if (info.hIcon != IntPtr.Zero)
            {
                using var guard = new IconGuard(info.hIcon);
                return ToImageSource(info.hIcon);
            }
        }
        catch (Exception)
        {
            // 落到注册表兜底
        }

        return GetFileIconFromRegistry(normalizedExt);
    }

    private static ImageSource? GetFolderIcon()
    {
        try
        {
            var info = new ShFileInfo();
            var flags = SHGFI_ICON | SHGFI_LARGEICON | SHGFI_USEFILEATTRIBUTES;
            SHGetFileInfo("folder", FILE_ATTRIBUTE_DIRECTORY, ref info,
                (uint)Marshal.SizeOf<ShFileInfo>(), flags);
            if (info.hIcon != IntPtr.Zero)
            {
                using var guard = new IconGuard(info.hIcon);
                return ToImageSource(info.hIcon);
            }
        }
        catch (Exception)
        {
            // 落到注册表兜底
        }

        // HKCR\Folder\DefaultIcon（形如 shell32.dll,3）
        return GetIconFromDefaultIconKey(@"Folder\DefaultIcon");
    }

    // ---------------- 策略二：注册表 DefaultIcon ----------------

    private static ImageSource? GetFileIconFromRegistry(string normalizedExt)
    {
        if (string.IsNullOrEmpty(normalizedExt)) return null;

        // 1) 扩展名 -> ProgID -> DefaultIcon
        try
        {
            var progId = Registry.ClassesRoot.OpenSubKey("." + normalizedExt)?.GetValue(null) as string;
            if (!string.IsNullOrEmpty(progId))
            {
                var icon = GetIconFromDefaultIconKey(progId + @"\DefaultIcon");
                if (icon is not null) return icon;
            }

            // 2) 某些扩展名直接在扩展名键下注册 DefaultIcon
            var direct = GetIconFromDefaultIconKey("." + normalizedExt + @"\DefaultIcon");
            if (direct is not null) return direct;
        }
        catch (Exception)
        {
            // 注册表不可读时放弃
        }

        return null;
    }

    /// <summary>
    /// 读取 HKCR\&lt;key&gt;\DefaultIcon（形如 "C:\a\b.dll,39" 或 "\"D:\x\y.ico\""）并提取图标。
    /// </summary>
    private static ImageSource? GetIconFromDefaultIconKey(string keyPath)
    {
        try
        {
            var raw = Registry.ClassesRoot.OpenSubKey(keyPath)?.GetValue(null) as string;
            if (string.IsNullOrWhiteSpace(raw)) return null;

            var (path, index) = ParseDefaultIcon(raw);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

            var icons = new IntPtr[1];
            var extracted = PrivateExtractIcons(path, index, 32, 32, icons, null, 1, 0);
            if (extracted > 0 && icons[0] != IntPtr.Zero)
            {
                using var guard = new IconGuard(icons[0]);
                return ToImageSource(icons[0]);
            }
        }
        catch (Exception)
        {
            // 任何异常都按“无图标”处理，由界面回退到文字徽标
        }

        return null;
    }

    /// <summary>解析 DefaultIcon 字符串：支持带引号路径、尾随 ,index、环境变量。</summary>
    private static (string Path, int Index) ParseDefaultIcon(string raw)
    {
        var s = Environment.ExpandEnvironmentVariables(raw.Trim());

        if (s.StartsWith('"'))
        {
            var end = s.IndexOf('"', 1);
            if (end < 0) return (s.Trim('"'), 0);
            var rest = s[(end + 1)..].TrimStart(',');
            return (s[1..end], int.TryParse(rest, out var quotedIndex) ? quotedIndex : 0);
        }

        var comma = s.LastIndexOf(',');
        if (comma < 0) return (s, 0);

        return (s[..comma], int.TryParse(s[(comma + 1)..], out var index) ? index : 0);
    }

    private static ImageSource? ToImageSource(IntPtr hIcon)
    {
        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(
                hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            // 冻结后可跨线程使用，避免每个卡片各持一份可变资源
            if (source is BitmapSource bs) bs.Freeze();
            return source;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>确保 HICON 一定会被销毁，避免注册表兜底路径泄漏图标句柄。</summary>
    private sealed class IconGuard : IDisposable
    {
        private IntPtr _handle;

        public IconGuard(IntPtr handle) => _handle = handle;

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                DestroyIcon(_handle);
                _handle = IntPtr.Zero;
            }
        }
    }

    /// <summary>归一化扩展名："pdf" / ".pdf" / "报告.pdf" → "pdf"。</summary>
    private static string NormalizeKey(string? extensionOrName)
    {
        if (string.IsNullOrWhiteSpace(extensionOrName)) return string.Empty;
        var s = extensionOrName.Trim().TrimStart('.');
        // 允许传入完整文件名
        var dot = s.LastIndexOf('.');
        return dot >= 0 ? s[(dot + 1)..] : s;
    }
}
