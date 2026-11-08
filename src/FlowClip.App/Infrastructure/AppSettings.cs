using System;
using System.IO;
using System.Text.Json;

namespace FlowClip.App.Infrastructure;

/// <summary>
/// 轻量本地设置：记住用户最后使用的窗口尺寸与主题偏好。
/// 存放于 %LOCALAPPDATA%/FlowClip/settings.json，读写失败静默回退默认值。
/// </summary>
public class AppSettings
{
    public double WindowWidth { get; set; } = 600;
    public double WindowHeight { get; set; } = 480;
    public bool RememberTheme { get; set; }
    public bool IsDark { get; set; }

    /// <summary>
    /// 是否已提示过「已驻留托盘」。首次启动时 Windows 常把新应用图标收进隐藏图标区，
    /// 弹一次气泡让用户知道它在哪，避免重复打扰。
    /// </summary>
    public bool TrayHintShown { get; set; }

    private static string FilePath
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FlowClip", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            var path = FilePath;
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var s = JsonSerializer.Deserialize<AppSettings>(json);
                if (s is not null) return s;
            }
        }
        catch (Exception)
        {
            // 忽略，使用默认
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (dir is not null) Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception)
        {
            // 忽略
        }
    }
}
