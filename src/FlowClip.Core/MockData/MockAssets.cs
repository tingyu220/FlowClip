using System;
using System.IO;

namespace FlowClip.Core.MockData;

/// <summary>
/// 为界面开发准备真实的本地 mock 素材。
///
/// 之所以要落真实文件（而不是只在数据里写路径字符串）：
/// 1. 文件 / 文件夹图标通过 Windows Shell 按扩展名解析，需要真实存在的路径才能拿到正确的图标与大小；
/// 2. 图片缩略图要真实经过一次解码管线（含按需 / 缓存 / 只读缩略图，不整文件入内存）才能被验证；
/// 3. “源文件已失效”状态需要至少一个确实不存在的路径作为对照。
///
/// 这些文件仅用于本地界面验证，全部位于用户目录，不写入剪贴板、不参与真实监听。
/// </summary>
public static class MockAssets
{
    /// <summary>mock 素材根目录：%LOCALAPPDATA%/FlowClip/mock</summary>
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FlowClip", "mock");

    /// <summary>零散文件目录（用于文件 / 多文件记录）</summary>
    public static string FilesDir => Path.Combine(Root, "files");

    /// <summary>文件夹记录指向的目录</summary>
    public static string AssetsDir => Path.Combine(Root, "assets");

    /// <summary>图片记录指向的缩略图目录</summary>
    public static string ImagesDir => Path.Combine(Root, "images");

    /// <summary>刻意不创建的目录：用于验证“源文件已失效”状态</summary>
    public static string DeletedDir => Path.Combine(Root, "deleted");

    /// <summary>取 files 目录下的某个文件路径。</summary>
    public static string Sample(string name) => Path.Combine(FilesDir, name);

    /// <summary>确保素材已存在；已存在则跳过（不重复写盘）。失败静默忽略。</summary>
    public static void EnsureCreated()
    {
        try
        {
            Directory.CreateDirectory(FilesDir);
            Directory.CreateDirectory(AssetsDir);
            Directory.CreateDirectory(ImagesDir);

            // 各类常用文档（内容不重要，扩展名决定 Shell 图标；体积按实际大小回填）
            WriteText(Sample("需求文档.pdf"), "%PDF-mock\nFlowClip 界面验证用占位文档。\n");
            WriteText(Sample("预算表.xlsx"), "name,amount\nFlowClip UI,0\n");
            WriteText(Sample("项目计划.docx"), "FlowClip M1 第二轮 UI 与交互修复计划（占位）\n");
            WriteText(Sample("说明.md"), "# FlowClip\n占位说明文件。\n");
            WriteText(Sample("配置.json"), "{ \"mock\": true }\n");
            WriteText(Sample("备注.txt"), "占位备注。\n");
            WriteText(Sample("数据.csv"), "id,value\n1,mock\n");

            // 文件夹记录：内含若干文件，用于体现目录条目的计数
            for (int i = 1; i <= 12; i++)
            {
                WriteText(Path.Combine(AssetsDir, $"asset_{i:00}.txt"), $"占位资源 {i}\n");
            }

            // 图片缩略图：真实可解码的 BMP，让缩略图管线走完整流程
            WriteGradientBmp(Path.Combine(ImagesDir, "设计稿_v3.bmp"), 240, 150,
                (10, 132, 255), (94, 92, 230));
            WriteGradientBmp(Path.Combine(ImagesDir, "参考图_截图模板.bmp"), 200, 260,
                (48, 209, 88), (0, 199, 190));
        }
        catch (Exception)
        {
            // 素材准备失败不影响主流程：界面会退化为文字徽标
        }
    }

    private static void WriteText(string path, string content)
    {
        if (!File.Exists(path)) File.WriteAllText(path, content);
    }

    /// <summary>
    /// 写一张 24 位真彩渐变 BMP。
    /// 使用纯托管字节流构造，避免为 mock 引入任何第三方或大型依赖。
    /// </summary>
    private static void WriteGradientBmp(string path, int width, int height,
        (byte r, byte g, byte b) from, (byte r, byte g, byte b) to)
    {
        if (File.Exists(path)) return;

        int stride = ((24 * width + 31) / 32) * 4;      // 每行按 4 字节对齐
        int pixelSize = stride * height;
        int fileSize = 54 + pixelSize;

        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);

        // BITMAPFILEHEADER
        bw.Write((byte)'B');
        bw.Write((byte)'M');
        bw.Write(fileSize);
        bw.Write((short)0);
        bw.Write((short)0);
        bw.Write(54);

        // BITMAPINFOHEADER
        bw.Write(40);
        bw.Write(width);
        bw.Write(height);
        bw.Write((short)1);
        bw.Write((short)24);
        bw.Write(0);
        bw.Write(pixelSize);
        bw.Write(2835); // 水平分辨率 ~72 DPI
        bw.Write(2835); // 垂直分辨率 ~72 DPI
        bw.Write(0);
        bw.Write(0);

        var row = new byte[stride];
        for (int y = 0; y < height; y++)
        {
            // BMP 自下而上存储
            int srcY = height - 1 - y;
            double ty = height <= 1 ? 0 : srcY / (double)(height - 1);
            Array.Clear(row, 0, stride);
            for (int x = 0; x < width; x++)
            {
                double tx = width <= 1 ? 0 : x / (double)(width - 1);
                // 对角线渐变，视觉上有层次即可
                double t = (tx * 0.5 + ty * 0.5);
                byte r = Lerp(from.r, to.r, t);
                byte g = Lerp(from.g, to.g, t);
                byte b = Lerp(from.b, to.b, t);

                int i = x * 3;
                row[i] = b;     // BMP 为 BGR 顺序
                row[i + 1] = g;
                row[i + 2] = r;
            }

            bw.Write(row, 0, stride);
        }
    }

    private static byte Lerp(byte a, byte b, double t)
        => (byte)(a + (b - a) * Math.Clamp(t, 0, 1));
}
