using System;
using System.Collections.Generic;
using System.IO;
using FlowClip.Core.Models;

namespace FlowClip.Core.MockData;

/// <summary>
/// 生成模拟剪贴板历史，覆盖六类记录：文字、图片、链接、文件、文件夹、多文件。
/// 仅用于界面开发与交互验证，真实剪贴板监听不会写入这些记录。
///
/// 文件 / 图片记录指向 <see cref="MockAssets"/> 准备好的真实本地文件，
/// 因此文件图标、缩略图、文件大小、失效判定走的都是真实逻辑，而非界面假数据。
/// </summary>
public static class MockDataFactory
{
    /// <summary>
    /// 生成一组覆盖全部类型的模拟记录。
    /// </summary>
    public static IReadOnlyList<ClipItem> CreateSampleItems()
    {
        // 先确保本地素材就位（已存在则跳过，不重复写盘）
        MockAssets.EnsureCreated();

        var now = DateTime.Now;
        var files = MockAssets.FilesDir;

        var items = new List<ClipItem>
        {
            // ===== 文字 =====
            new ClipItem
            {
                Type = ClipItemType.Text,
                Title = "FlowClip 周会纪要：M1 阶段完成剪贴板历史面板与全局唤出",
                Preview = "待办：补充多文件记录样式、确认 Acrylic 在 Win10 的回退表现……",
                Timestamp = now.AddMinutes(-2),
                SourceApp = "记事本",
                IsPinned = true
            },
            new ClipItem
            {
                Type = ClipItemType.Text,
                Title = "SELECT id, title FROM clips WHERE pinned = 1 ORDER BY ts DESC;",
                Preview = "一段常用的 SQL 查询片段，便于快速粘贴。",
                Timestamp = now.AddMinutes(-9),
                SourceApp = "VS Code"
            },

            // ===== 链接 =====
            new ClipItem
            {
                Type = ClipItemType.Link,
                Title = "https://learn.microsoft.com/dotnet/desktop/wpf/",
                Preview = "Windows Presentation Foundation 文档中心",
                Timestamp = now.AddMinutes(-14),
                SourceApp = "Microsoft Edge"
            },
            new ClipItem
            {
                Type = ClipItemType.Link,
                Title = "https://github.com/tingyu220/FlowClip",
                Preview = "FlowClip 仓库主页",
                Timestamp = now.AddMinutes(-26),
                SourceApp = "Microsoft Edge"
            },

            // ===== 图片 =====
            // 注意：图片来源需区分——FlowClip 自发起的截图才能标记为截图，
            // 第三方写入剪贴板的图片只作为普通图片，不默认认定为截图。
            new ClipItem
            {
                Type = ClipItemType.Image,
                Title = "设计稿_v3.bmp",
                Preview = "240 × 150 · BMP",
                Timestamp = now.AddMinutes(-33),
                SourceApp = "FlowClip 截图",
                SizeBytes = FileSize(Path.Combine(MockAssets.ImagesDir, "设计稿_v3.bmp")),
                ImagePath = Path.Combine(MockAssets.ImagesDir, "设计稿_v3.bmp")
            },
            new ClipItem
            {
                Type = ClipItemType.Image,
                Title = "参考图_截图模板.bmp",
                Preview = "200 × 260 · BMP",
                Timestamp = now.AddMinutes(-71),
                SourceApp = "Edge（复制图片）",
                SizeBytes = FileSize(Path.Combine(MockAssets.ImagesDir, "参考图_截图模板.bmp")),
                ImagePath = Path.Combine(MockAssets.ImagesDir, "参考图_截图模板.bmp")
            },

            // ===== 文件 =====
            FileItem("需求文档.pdf", files, now.AddMinutes(-48), "资源管理器"),
            FileItem("预算表.xlsx", files, now.AddHours(-2), "资源管理器"),
            FileItem("项目计划.docx", files, now.AddHours(-4), "资源管理器"),

            // ===== 文件夹 =====
            new ClipItem
            {
                Type = ClipItemType.Folder,
                Title = "assets",
                Preview = MockAssets.AssetsDir,
                Timestamp = now.AddHours(-3),
                SourceApp = "资源管理器",
                FileCount = CountFiles(MockAssets.AssetsDir),
                FilePaths = new List<string> { MockAssets.AssetsDir }
            },

            // ===== 多文件 =====
            new ClipItem
            {
                Type = ClipItemType.MultiFile,
                Title = "3 个文件",
                Preview = "说明.md、配置.json、备注.txt",
                Timestamp = now.AddHours(-5),
                SourceApp = "资源管理器",
                FileCount = 3,
                SizeBytes = FileSize(Path.Combine(files, "说明.md"))
                          + FileSize(Path.Combine(files, "配置.json"))
                          + FileSize(Path.Combine(files, "备注.txt")),
                FilePaths = new List<string>
                {
                    Path.Combine(files, "说明.md"),
                    Path.Combine(files, "配置.json"),
                    Path.Combine(files, "备注.txt")
                }
            },

            // 刻意保留一条“源文件已失效”的对照：位于未创建的 deleted 目录
            new ClipItem
            {
                Type = ClipItemType.MultiFile,
                Title = "2 个文件（已移动）",
                Preview = "旧版总结.docx、归档数据.csv",
                Timestamp = now.AddHours(-8),
                SourceApp = "资源管理器",
                FileCount = 2,
                FilePaths = new List<string>
                {
                    Path.Combine(MockAssets.DeletedDir, "旧版总结.docx"),
                    Path.Combine(MockAssets.DeletedDir, "归档数据.csv")
                }
            },

            // 再补一条文字，丰富列表
            new ClipItem
            {
                Type = ClipItemType.Text,
                Title = "git commit -m \"feat: 历史面板 UI 第二轮\"",
                Preview = "提交信息模板，记得先本地验证再推送。",
                Timestamp = now.AddHours(-1),
                SourceApp = "终端"
            }
        };

        return items;
    }

    private static ClipItem FileItem(string fileName, string dir, DateTime time, string sourceApp)
    {
        var path = Path.Combine(dir, fileName);
        return new ClipItem
        {
            Type = ClipItemType.File,
            Title = fileName,
            Preview = path,
            Timestamp = time,
            SourceApp = sourceApp,
            SizeBytes = FileSize(path),
            FilePaths = new List<string> { path }
        };
    }

    private static long? FileSize(string path)
    {
        try { return File.Exists(path) ? new FileInfo(path).Length : null; }
        catch { return null; }
    }

    private static int CountFiles(string dir)
    {
        try { return Directory.Exists(dir) ? Directory.GetFiles(dir).Length : 0; }
        catch { return 0; }
    }
}
