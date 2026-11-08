using FlowClip.Core.Models;
using FlowClip.Core.Services;
using Xunit;

namespace FlowClip.Tests;

/// <summary>
/// 验证历史存储服务的搜索 / 筛选 / 置顶 / 删除逻辑（纯逻辑，不依赖 Windows）。
/// </summary>
public class ClipHistoryServiceTests
{
    private static ClipHistoryService CreateFilled()
    {
        var svc = new ClipHistoryService();
        svc.Add(new ClipItem { Type = ClipItemType.Text, Title = "Apple 风格的剪贴板", Preview = "备注", SourceApp = "记事本" });
        svc.Add(new ClipItem { Type = ClipItemType.Link, Title = "https://dot.net", Preview = "微软", SourceApp = "Edge" });
        svc.Add(new ClipItem { Type = ClipItemType.File, Title = "报告.pdf", SourceApp = "资源管理器" });
        return svc;
    }

    [Fact]
    public void Search_ByKeyword_MatchesTitlePreviewAndSource()
    {
        var svc = CreateFilled();

        Assert.Contains(svc.GetFiltered("apple", null), x => x.Type == ClipItemType.Text);
        Assert.Contains(svc.GetFiltered("dot.net", null), x => x.Type == ClipItemType.Link);
        Assert.Contains(svc.GetFiltered("记事本", null), x => x.Type == ClipItemType.Text);
        Assert.DoesNotContain(svc.GetFiltered("zzz不存在", null), x => true);
    }

    [Fact]
    public void Filter_ByType_ReturnsOnlyThatType()
    {
        var svc = CreateFilled();

        var links = svc.GetFiltered(null, ClipItemType.Link);
        Assert.All(links, x => Assert.Equal(ClipItemType.Link, x.Type));
        Assert.Single(links);
    }

    [Fact]
    public void TogglePin_MovesPinnedToFront()
    {
        var svc = CreateFilled();
        var file = svc.GetFiltered(null, ClipItemType.File).First();
        svc.TogglePin(file.Id);

        Assert.True(svc.Items[0].IsPinned);
        Assert.Equal(file.Id, svc.Items[0].Id);
    }

    [Fact]
    public void Remove_DeletesById()
    {
        var svc = CreateFilled();
        var link = svc.GetFiltered(null, ClipItemType.Link).First();
        var ok = svc.Remove(link.Id);

        Assert.True(ok);
        Assert.DoesNotContain(svc.Items, x => x.Id == link.Id);
    }
}
