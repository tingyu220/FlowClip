using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Media;
using FlowClip.App.Infrastructure;
using FlowClip.Core.Models;

namespace FlowClip.App.Converters;

/// <summary>
/// 轻量 ICommand 实现，支持带参数的同步执行。
/// </summary>
public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute is null || _canExecute(parameter);

    public void Execute(object? parameter) => _execute(parameter);

    public event EventHandler? CanExecuteChanged;
}

/// <summary>bool -> Visibility（true 显示）</summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => (value is bool b && b) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility v && v == Visibility.Visible;
}

/// <summary>字符串为空 -> Visible（用于占位提示）；非空 -> Collapsed</summary>
[ValueConversion(typeof(string), typeof(Visibility))]
public class StringEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>值为 null 或空 -> Collapsed（用于卡片可选字段，如预览 / 来源 / 大小）</summary>
[ValueConversion(typeof(object), typeof(Visibility))]
public class HideWhenEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return Visibility.Collapsed;
        if (value is string s && string.IsNullOrEmpty(s)) return Visibility.Collapsed;
        if (value is long l && l <= 0) return Visibility.Collapsed;
        if (value is int i && i <= 0) return Visibility.Collapsed;
        return Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>字节数 -> 友好大小，如 2.3 MB</summary>
[ValueConversion(typeof(long?), typeof(string))]
public class FileSizeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not long bytes || bytes <= 0)
        {
            return string.Empty;
        }

        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.##} {units[unit]}";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>时间 -> 相对时间，如 刚刚 / 3 分钟前 / 2 小时前 / 昨天 / 日期</summary>
[ValueConversion(typeof(DateTime), typeof(string))]
public class RelativeTimeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTime dt)
        {
            return string.Empty;
        }

        var diff = DateTime.Now - dt;
        if (diff.TotalSeconds < 30) return "刚刚";
        if (diff.TotalMinutes < 1) return $"{(int)diff.TotalSeconds} 秒前";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} 分钟前";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours} 小时前";
        if (diff.TotalDays < 2) return "昨天";
        if (diff.TotalDays < 7) return $"{(int)diff.TotalDays} 天前";
        return dt.ToString("MM-dd", culture);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>ClipItemType -> 中文单字徽标</summary>
[ValueConversion(typeof(ClipItemType), typeof(string))]
public class TypeToGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is ClipItemType t ? t switch
        {
            ClipItemType.Text => "文",
            ClipItemType.Image => "图",
            ClipItemType.Link => "链",
            ClipItemType.File => "件",
            ClipItemType.Folder => "夹",
            ClipItemType.MultiFile => "多",
            _ => "?"
        } : "?";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>ClipItemType -> 徽标背景画刷（主题无关）</summary>
[ValueConversion(typeof(ClipItemType), typeof(Brush))]
public class TypeToBadgeBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is ClipItemType t ? new SolidColorBrush(t switch
        {
            ClipItemType.Text => Color.FromRgb(52, 199, 89),
            ClipItemType.Image => Color.FromRgb(10, 132, 255),
            ClipItemType.Link => Color.FromRgb(94, 92, 230),
            ClipItemType.File => Color.FromRgb(255, 159, 10),
            ClipItemType.Folder => Color.FromRgb(255, 204, 0),
            ClipItemType.MultiFile => Color.FromRgb(255, 55, 95),
            _ => Color.FromRgb(142, 142, 147),
        }) : new SolidColorBrush(Colors.Gray);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 多值转换：判断筛选芯片是否与当前激活筛选匹配。
/// values[0] = 芯片对应的类型（可为 null 表示“全部”）；values[1] = 当前激活筛选。
/// </summary>
public class FilterMatchMultiConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var chip = values.Length > 0 ? values[0] as ClipItemType? : null;
        var active = values.Length > 1 ? values[1] as ClipItemType? : null;
        return Equals(chip, active);
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>筛选芯片是否选中：比较当前激活筛选与芯片对应的类型（参数）。</summary>
public class FilterIsCheckedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var active = value as ClipItemType?;
        var chip = parameter as ClipItemType?;
        return Equals(active, chip);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>文件 / 文件夹 / 多文件类型 -> 系统 Shell 图标；其他类型返回 null（回退到文字徽标）。</summary>
[ValueConversion(typeof(ClipItem), typeof(ImageSource))]
public class FileIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ClipItem item) return null;

        switch (item.Type)
        {
            // 文件 / 文件夹：直接取自身的 Shell 图标
            case ClipItemType.File:
            case ClipItemType.Folder:
                return ShellIconHelper.GetIconForExtension(
                    ExtensionOf(item.FilePaths?.FirstOrDefault()), item.Type == ClipItemType.Folder);

            // 多文件：用第一个文件的图标代表整组，另由数量角标体现“多”
            case ClipItemType.MultiFile:
                return ShellIconHelper.GetIconForExtension(
                    ExtensionOf(item.FilePaths?.FirstOrDefault()), false);

            default:
                return null;
        }
    }

    private static string? ExtensionOf(string? path)
        => path is null ? null : Path.GetExtension(path).TrimStart('.');

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>文件 / 文件夹 / 多文件 -> Visible（显示系统图标）；其他 -> Collapsed。</summary>
[ValueConversion(typeof(ClipItem), typeof(Visibility))]
public class FileIconVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is ClipItem item &&
           (item.Type == ClipItemType.File ||
            item.Type == ClipItemType.Folder ||
            item.Type == ClipItemType.MultiFile)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 文字 / 链接 -> Visible（显示彩色文字徽标）；图片且已有缩略图 -> Collapsed；
/// 文件 / 文件夹 / 多文件 -> Collapsed（改由系统图标承担）。
/// </summary>
[ValueConversion(typeof(ClipItem), typeof(Visibility))]
public class BadgeVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ClipItem item) return Visibility.Visible;
        if (item.Type is ClipItemType.File or ClipItemType.Folder or ClipItemType.MultiFile)
            return Visibility.Collapsed;
        // 图片拿到真实缩略图后就不必再用文字徽标
        if (item.Type == ClipItemType.Image &&
            ThumbnailHelper.GetThumbnail(item.ImagePath) is not null)
            return Visibility.Collapsed;
        return Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 图片条目 -> 真实缩略图。
/// 仅在 ImagePath 指向真实存在的文件时返回缩略图；否则返回 null，由界面回退到文字徽标。
/// 配合 XAML 的 Binding IsAsync=True 在后台线程解码，不阻塞 UI。
/// </summary>
[ValueConversion(typeof(ClipItem), typeof(ImageSource))]
public class ImageThumbConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is ClipItem { Type: ClipItemType.Image } item
            ? ThumbnailHelper.GetThumbnail(item.ImagePath)
            : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>图片条目且有可用缩略图 -> Visible。</summary>
[ValueConversion(typeof(ClipItem), typeof(Visibility))]
public class ImageThumbVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is ClipItem { Type: ClipItemType.Image } item &&
           ThumbnailHelper.GetThumbnail(item.ImagePath) is not null
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 源文件缺失 -> Visible。用于文件 / 文件夹 / 多文件记录的失效提示。
/// 全部路径都不存在时判定为失效（部分存在时不算失效）。
/// </summary>
[ValueConversion(typeof(ClipItem), typeof(Visibility))]
public class MissingVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is ClipItem item && IsMissing(item) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    /// <summary>是否为失效条目（无路径列表，或全部路径均已不存在）。</summary>
    public static bool IsMissing(ClipItem item)
    {
        if (item.FilePaths is null || item.FilePaths.Count == 0) return false;
        foreach (var p in item.FilePaths)
        {
            try
            {
                if (File.Exists(p) || Directory.Exists(p)) return false;
            }
            catch
            {
                // 非法路径视为不存在
            }
        }

        return true;
    }
}
