using System.Text.RegularExpressions;

namespace DshDesktop.Tests;

/// <summary>
/// UpdatesView DSH Runtime 卡片三态结构守卫（2026-09-18 实机投诉重设计）：
/// 当前版本已等于通道最新版时，卡片仍显示「有可用更新」且「安装最新版本」按钮常显可重复点击。
/// 根因是 badge 可见性裸绑 <c>LatestDshVersion IsNotNullOrEmpty</c>（该字段检查成功后始终非空），
/// 与版本是否相等无关。卡片必须改由 <see cref="DshDesktop.Presentation.Avalonia.Features.Updates.UpdatesState.DshStage"/>
/// 三态驱动：UpToDate（已是最新，无操作按钮）/ Available（安装最新版本）/ ReadyToActivate（激活并切换）。
/// 这些是 XAML 编译器看不见的语义约束（改回去编译照过），故由本测试固化。
/// </summary>
public sealed partial class UpdatesDshStageGuardTests
{
    [GeneratedRegex("<Button[^>]*OnInstallLatestClicked[^>]*>", RegexOptions.Singleline)]
    private static partial Regex InstallButtonTag();

    [GeneratedRegex("<Button[^>]*OnActivateLatestClicked[^>]*>", RegexOptions.Singleline)]
    private static partial Regex ActivateLatestButtonTag();

    /// <summary>
    /// badge 禁止再裸绑 <c>LatestDshVersion</c> 非空判定——那正是「已是最新仍显示有可用更新」的根因；
    /// 三态 badge（已是最新 / 有可用更新 / 待激活）必须经 DshStage 派生阶段驱动。
    /// </summary>
    [Test]
    public async Task UpdatesView_Badge_DrivenByDshStage()
    {
        var text = await ReadViewAsync();

        await Assert.That(text.Contains(
            "IsVisible=\"{Binding LatestDshVersion, Converter={x:Static StringConverters.IsNotNullOrEmpty}}\"",
            StringComparison.Ordinal)).IsFalse();
        await Assert.That(text.Contains("已是最新", StringComparison.Ordinal)).IsTrue();
        await Assert.That(text.Contains("待激活", StringComparison.Ordinal)).IsTrue();
        await Assert.That(text.Contains("Binding DshStage", StringComparison.Ordinal)).IsTrue();
    }

    /// <summary>
    /// 「安装最新版本」按钮仅在 Available（最新版不在本机）时出现，必须带 DshStage 可见性绑定——
    /// 常显按钮会让用户对本机已有的版本重复下载安装。
    /// </summary>
    [Test]
    public async Task UpdatesView_InstallButton_OnlyWhenAvailable()
    {
        var text = await ReadViewAsync();

        Match tag = InstallButtonTag().Match(text);
        await Assert.That(tag.Success).IsTrue();
        await Assert.That(tag.Value.Contains("IsVisible", StringComparison.Ordinal)).IsTrue();
        await Assert.That(tag.Value.Contains("DshStage", StringComparison.Ordinal)).IsTrue();
    }

    /// <summary>
    /// ReadyToActivate 阶段的主按钮是「激活并切换」（复用激活二次确认），引导用户完成版本切换，
    /// 而不是再次安装；同样必须带 DshStage 可见性绑定。
    /// </summary>
    [Test]
    public async Task UpdatesView_ActivateLatestButton_OnlyWhenReadyToActivate()
    {
        var text = await ReadViewAsync();

        Match tag = ActivateLatestButtonTag().Match(text);
        await Assert.That(tag.Success).IsTrue();
        await Assert.That(tag.Value.Contains("IsVisible", StringComparison.Ordinal)).IsTrue();
        await Assert.That(tag.Value.Contains("ReadyToActivate", StringComparison.Ordinal)).IsTrue();
    }

    private static async Task<string> ReadViewAsync()
    {
        var root = XamlScan.FindRepositoryRoot();
        await Assert.That(root).IsNotNull();

        var path = Path.Combine(
            root!, "src", "DshDesktop.Presentation.Avalonia", "Features", "Updates", "UpdatesView.axaml");
        await Assert.That(File.Exists(path)).IsTrue();
        return XamlScan.StripComments(await File.ReadAllTextAsync(path));
    }
}
