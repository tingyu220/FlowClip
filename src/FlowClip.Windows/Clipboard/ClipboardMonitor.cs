using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using FlowClip.Windows.Native;

namespace FlowClip.Windows.Clipboard;

/// <summary>
/// 剪贴板变化事件参数，仅携带格式摘要，不含任何数据拷贝。
/// </summary>
public sealed class ClipboardChangeEventArgs : EventArgs
{
    public string Summary { get; }

    public ClipboardChangeEventArgs(string summary) => Summary = summary;
}

/// <summary>
/// 基于 AddClipboardFormatListener + WM_CLIPBOARDUPDATE 的剪贴板变化监听器。
/// 仅做独立技术验证：检测到变化后写入本地日志文件（静默、不弹窗、不写入历史 UI）。
/// 关键点：不将所有剪贴板图片误判为截图——此处只记录格式名称摘要。
/// </summary>
public sealed class ClipboardMonitor : IDisposable
{
    private readonly MessageWindow _window;
    private readonly string _logPath;
    private bool _disposed;

    public event EventHandler<ClipboardChangeEventArgs>? ClipboardChanged;

    public ClipboardMonitor(MessageWindow window, string logDirectory)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        Directory.CreateDirectory(logDirectory);
        _logPath = Path.Combine(logDirectory, "clipboard.log");

        _window.MessageReceived += OnMessage;

        if (!Win32.AddClipboardFormatListener(_window.Handle))
        {
            Log("AddClipboardFormatListener 失败，错误码: " + Marshal.GetLastWin32Error());
        }
        else
        {
            Log("剪贴板监听器已注册（WM_CLIPBOARDUPDATE）");
        }
    }

    private void OnMessage(object? sender, Win32.WindowMessage e)
    {
        if (e.Msg != Win32.WM_CLIPBOARDUPDATE)
        {
            return;
        }

        var summary = ReadClipboardSummary();
        Log("剪贴板变化 -> " + summary);
        ClipboardChanged?.Invoke(this, new ClipboardChangeEventArgs(summary));
    }

    /// <summary>
    /// 读取当前剪贴板可用格式名称（只读，无副作用），用于验证监听而非消费数据。
    /// </summary>
    private string ReadClipboardSummary()
    {
        if (!Win32.OpenClipboard(_window.Handle))
        {
            return "（剪贴板被占用，无法读取格式）";
        }

        try
        {
            var count = Win32.CountClipboardFormats();
            var formats = new List<string>();
            uint fmt = 0;
            while ((fmt = Win32.EnumClipboardFormats(fmt)) != 0)
            {
                formats.Add(ResolveFormatName(fmt));
                if (formats.Count > 16)
                {
                    break;
                }
            }

            return $"格式数={count}; 主要格式=[{string.Join(", ", formats)}]";
        }
        catch (Exception ex)
        {
            return "（读取异常: " + ex.Message + "）";
        }
        finally
        {
            Win32.CloseClipboard();
        }
    }

    private static string ResolveFormatName(uint format)
    {
        // 标准预定义剪贴板格式
        var known = new Dictionary<uint, string>
        {
            { 1, "CF_TEXT" },
            { 2, "CF_BITMAP" },
            { 3, "CF_METAFILEPICT" },
            { 6, "CF_TIFF" },
            { 7, "CF_OEMTEXT" },
            { 8, "CF_DIB" },
            { 9, "CF_PALETTE" },
            { 11, "CF_RIFF" },
            { 12, "CF_WAVE" },
            { 13, "CF_UNICODETEXT" },
            { 14, "CF_ENHMETAFILE" },
            { 15, "CF_HDROP" },
            { 16, "CF_LOCALE" },
            { 17, "CF_DIBV5" },
        };

        if (known.TryGetValue(format, out var name))
        {
            return name;
        }

        var sb = new StringBuilder(256);
        var len = Win32.GetClipboardFormatName(format, sb, sb.Capacity);
        if (len > 0)
        {
            return sb.ToString(0, len);
        }

        return "格式#" + format.ToString(CultureInfo.InvariantCulture);
    }

    private void Log(string line)
    {
        try
        {
            var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {line}{Environment.NewLine}";
            File.AppendAllText(_logPath, entry);
        }
        catch
        {
            // 日志失败不应影响主流程
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
        Win32.RemoveClipboardFormatListener(_window.Handle);
        Log("剪贴板监听器已移除");
    }
}
