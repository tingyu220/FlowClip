using System.Collections.Generic;

namespace FlowClip.Core.Models;

/// <summary>
/// 一条剪贴板历史记录。
/// 采用单一扁平模型 + 类型枚举，避免为六类记录各建一套继承体系（保持轻量 MVVM）。
/// 文件类记录（File / Folder / MultiFile）只保存路径引用，不复制文件本体。
/// </summary>
public class ClipItem
{
    /// <summary>唯一标识</summary>
    public string Id { get; set; } = System.Guid.NewGuid().ToString("N");

    /// <summary>条目类型</summary>
    public ClipItemType Type { get; set; }

    /// <summary>主显示文本：文字内容摘要 / 文件名 / 链接地址</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>次级描述：例如链接标题、文件信息</summary>
    public string Preview { get; set; } = string.Empty;

    /// <summary>进入历史的时间</summary>
    public System.DateTime Timestamp { get; set; } = System.DateTime.Now;

    /// <summary>来源应用名</summary>
    public string SourceApp { get; set; } = string.Empty;

    /// <summary>是否置顶</summary>
    public bool IsPinned { get; set; }

    /// <summary>文件大小（字节），仅文件类记录使用</summary>
    public long? SizeBytes { get; set; }

    /// <summary>
    /// 图片缩略图路径。按需加载：真实模式下才读取磁盘；
    /// Mock 数据下为占位标识，由界面渲染渐变占位图。
    /// </summary>
    public string? ImagePath { get; set; }

    /// <summary>文件类记录保存的路径引用列表</summary>
    public IReadOnlyList<string>? FilePaths { get; set; }

    /// <summary>文件数量（多文件 / 文件夹内容计数）</summary>
    public int? FileCount { get; set; }

    /// <summary>类型中文标签（界面展示用）</summary>
    public string TypeLabel => Type switch
    {
        ClipItemType.Text => "文字",
        ClipItemType.Image => "图片",
        ClipItemType.Link => "链接",
        ClipItemType.File => "文件",
        ClipItemType.Folder => "文件夹",
        ClipItemType.MultiFile => "多文件",
        _ => "未知"
    };
}
