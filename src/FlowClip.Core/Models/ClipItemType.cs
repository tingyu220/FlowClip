namespace FlowClip.Core.Models;

/// <summary>
/// 剪贴板条目类型。
/// </summary>
public enum ClipItemType
{
    /// <summary>文字</summary>
    Text,

    /// <summary>图片</summary>
    Image,

    /// <summary>链接</summary>
    Link,

    /// <summary>文件</summary>
    File,

    /// <summary>文件夹</summary>
    Folder,

    /// <summary>多文件</summary>
    MultiFile
}
