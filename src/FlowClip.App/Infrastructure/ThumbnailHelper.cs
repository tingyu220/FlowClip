using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FlowClip.App.Infrastructure;

/// <summary>
/// 图片缩略图加载器。
///
/// 设计约束（来自性能要求）：
/// - 只解码到目标宽度（DecodePixelWidth），不为预览把整张大图读入内存；
/// - 解码后 Freeze，可跨线程持有，避免每个卡片各自持有一份可变资源；
/// - 采用有界缓存（LRU 簡化版：超出上限清空），防止长历史下缓存无上限增长；
/// - OnLoad 模式一次性读入并立即释放文件句柄，避免锁定源文件。
/// </summary>
public static class ThumbnailHelper
{
    private const int MaxCacheEntries = 120;

    private static readonly Dictionary<string, BitmapSource?> Cache =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Lock = new();

    /// <summary>
    /// 取图片文件的缩略图。文件不存在或无法解码时返回 null（调用方回退到占位徽标）。
    /// 该方法会被 WPF 在后台线程调用（Binding IsAsync=True），因此不得触碰 UI 线程状态。
    /// </summary>
    public static BitmapSource? GetThumbnail(string? path, int decodeWidth = 96)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (!File.Exists(path)) return null;

        lock (Lock)
        {
            if (Cache.TryGetValue(path, out var cached)) return cached;
        }

        BitmapSource? result = null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.DecodePixelWidth = decodeWidth;   // 关键：按目标尺寸解码，避免全尺寸入内存
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.EndInit();
            bmp.Freeze();
            result = bmp;
        }
        catch (Exception)
        {
            result = null;
        }

        lock (Lock)
        {
            if (Cache.Count >= MaxCacheEntries) Cache.Clear();
            Cache[path] = result;
        }

        return result;
    }

    /// <summary>清空缩略图缓存（切换主题或长时间隐藏面板后可调用）。</summary>
    public static void ClearCache()
    {
        lock (Lock) Cache.Clear();
    }
}
