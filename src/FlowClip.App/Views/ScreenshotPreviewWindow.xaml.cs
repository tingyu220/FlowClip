using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FlowClip.App.Infrastructure;

namespace FlowClip.App.Views;

/// <summary>
/// 右上角截图预览（轻量通知，不是对话框）。
///
/// 关键约定：
/// - ShowActivated=false + Focusable=false：出现时不抢占也不持有键盘焦点，不打断当前操作；
/// - 默认 4 秒自动收起，鼠标悬停则暂停计时，移出后重新计时；
/// - 出现为「轻微位移 + 淡入」，收起为纯淡出，不播放常驻动画；
/// - 预览图按目标尺寸解码（走 ThumbnailHelper 缓存），不把大图整张读进内存；
/// - 按钮只绑定真实能力：贴屏尚未实现，因此显式禁用并给出说明，不做「看起来能点」的假按钮。
/// </summary>
public partial class ScreenshotPreviewWindow : Window
{
    // 自动收起时间
    private static readonly TimeSpan AutoHideDelay = TimeSpan.FromSeconds(4);

    // 尺寸上限：整窗最宽 280；图片区域在此基础上留出内边距
    private const double MaxImageWidth = 252;
    private const double MaxImageHeight = 300;

    private DispatcherTimer? _autoHideTimer;

    public event EventHandler? RequestedHide;

    public ScreenshotPreviewWindow()
    {
        InitializeComponent();
        _autoHideTimer = new DispatcherTimer { Interval = AutoHideDelay };
        _autoHideTimer.Tick += (_, _) => Dismiss();
    }

    /// <summary>
    /// 设置预览内容。
    /// </summary>
    /// <param name="path">图片文件路径；不存在时显示占位背景。</param>
    /// <param name="caption">来源说明，用于区分自发起截图与第三方复制的图片。</param>
    public void SetPreview(string? path, string caption)
    {
        SourceLabel.Text = caption;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            PreviewImage.Source = null;
            ImageHost.Width = MaxImageWidth;
            ImageHost.Height = 140;
            return;
        }

        try
        {
            // 先只读元数据拿到原始宽高（不解码像素），用于等比缩放到上限内
            var decoder = BitmapDecoder.Create(new Uri(path, UriKind.Absolute),
                BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            if (decoder.Frames.Count == 0) return;

            var frame = decoder.Frames[0];
            double pw = Math.Max(1, frame.PixelWidth);
            double ph = Math.Max(1, frame.PixelHeight);
            double scale = Math.Min(Math.Min(MaxImageWidth / pw, MaxImageHeight / ph), 1.0);

            var targetW = (int)Math.Round(pw * scale);
            var targetH = (int)Math.Round(ph * scale);

            ImageHost.Width = targetW;
            ImageHost.Height = targetH;

            // 按 DPI 放大解码宽度，保证高分屏下不糊；解码一次后进入缓存
            int decodeWidth = (int)Math.Ceiling(targetW * GetDpiScale());
            PreviewImage.Source = ThumbnailHelper.GetThumbnail(path, decodeWidth);
        }
        catch (Exception)
        {
            PreviewImage.Source = null;
        }
    }

    /// <summary>定位到当前显示器右上角（间距 20px，符合 16–24px 要求）。</summary>
    public void PositionTopRight()
    {
        UpdateLayout();
        const double margin = 20;
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - ActualWidth - margin;
        Top = wa.Top + margin;
    }

    /// <summary>显示预览并启动自动收起计时。</summary>
    public new void Show()
    {
        base.Show();
        PlayEnterAnimation();
        _autoHideTimer?.Start();
    }

    /// <summary>收起预览：淡出后真正隐藏，避免在后台继续渲染。</summary>
    public void Dismiss()
    {
        _autoHideTimer?.Stop();

        var fade = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(160)));
        fade.Completed += (_, _) =>
        {
            Hide();
            Root.Opacity = 1;
            RequestedHide?.Invoke(this, EventArgs.Empty);
        };
        Root.BeginAnimation(OpacityProperty, fade);
    }

    private void PlayEnterAnimation()
    {
        Root.BeginAnimation(OpacityProperty, null);
        Root.Opacity = 0;
        Root.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(200))));

        Slide.X = 14;
        Slide.BeginAnimation(TranslateTransform.XProperty, null);
        Slide.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(14, 0, new Duration(TimeSpan.FromMilliseconds(220)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }

    /// <summary>鼠标悬停时暂停自动收起。</summary>
    private void OnMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        => _autoHideTimer?.Stop();

    /// <summary>鼠标移出后重新开始计时。</summary>
    private void OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        => _autoHideTimer?.Start();

    private double GetDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null) return 1.0;
        var m = source.CompositionTarget.TransformToDevice;
        return Math.Max(1.0, m.M11);
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (PreviewImage.Source is BitmapSource bmp)
        {
            try
            {
                Clipboard.SetImage(bmp);
            }
            catch (Exception)
            {
                // 忽略：剪贴板被占用时的常规失败
            }
        }

        Dismiss();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Dismiss();
}
