using DshDesktop.Domain.Plugins;
using DshDesktop.Presentation.Avalonia.Features.Plugins;

namespace DshDesktop.Tests;

/// <summary>
/// Plugins 页行投影测试（Phase 8 Issue 06，原型 plugins section 170-186 行）：
/// 客户端搜索过滤、状态 tag 与操作列文案映射、加载耗时列恒 "—"。
/// </summary>
public sealed class PluginRowProjectionTests
{
    private static readonly PluginInfo[] Sample =
    [
        new("dsh-better-sidebar", "1.3.1", false, true, "侧栏增强"),
        new("dshmarket", "1.13.1", true, true, ""),
        new("dsh-vision-router", "0.4.0", false, false, ""),
    ];

    [Test]
    public async Task Filter_EmptyQuery_ReturnsAll()
    {
        IReadOnlyList<PluginRow> rows = PluginRowProjection.Filter(Sample, "");

        await Assert.That(rows.Count).IsEqualTo(3);
    }

    [Test]
    public async Task Filter_NullOrWhitespaceQuery_ReturnsAll()
    {
        await Assert.That(PluginRowProjection.Filter(Sample, null).Count).IsEqualTo(3);
        await Assert.That(PluginRowProjection.Filter(Sample, "   ").Count).IsEqualTo(3);
    }

    [Test]
    public async Task Filter_MatchesNameCaseInsensitive()
    {
        IReadOnlyList<PluginRow> rows = PluginRowProjection.Filter(Sample, "VISION");

        await Assert.That(rows.Count).IsEqualTo(1);
        await Assert.That(rows[0].Name).IsEqualTo("dsh-vision-router");
    }

    [Test]
    public async Task Filter_NoMatch_ReturnsEmpty()
    {
        IReadOnlyList<PluginRow> rows = PluginRowProjection.Filter(Sample, "不存在");

        await Assert.That(rows.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Row_EnabledPlugin_GreenStatusAndManageAction()
    {
        PluginRow row = new PluginRow(Sample[0]);

        await Assert.That(row.StatusText).IsEqualTo("● 正常");
        await Assert.That(row.StatusIsInfo).IsFalse();
        await Assert.That(row.ShowEnable).IsFalse();
        await Assert.That(row.ShowManage).IsTrue();
    }

    [Test]
    public async Task Row_DisabledPlugin_InfoStatusAndEnableAction()
    {
        PluginRow row = new PluginRow(Sample[2]);

        await Assert.That(row.StatusText).IsEqualTo("○ 已禁用");
        await Assert.That(row.StatusIsInfo).IsTrue();
        await Assert.That(row.ShowEnable).IsTrue();
        await Assert.That(row.ShowManage).IsFalse();
    }

    [Test]
    public async Task Row_CorePlugin_NoActions()
    {
        PluginRow row = new PluginRow(Sample[1]);

        await Assert.That(row.ShowEnable).IsFalse();
        await Assert.That(row.ShowManage).IsFalse();
    }

    [Test]
    public async Task Row_LoadTime_AlwaysDash()
    {
        // DSH 无插件加载耗时数据，列保留为原型视觉（Phase 8 spec Round 2 Q2）。
        await Assert.That(new PluginRow(Sample[0]).LoadTime).IsEqualTo("—");
        await Assert.That(new PluginRow(Sample[2]).LoadTime).IsEqualTo("—");
    }

    [Test]
    public async Task CountText_FormatsTotal()
    {
        await Assert.That(PluginRowProjection.CountText(6)).IsEqualTo("6 Plugins");
        await Assert.That(PluginRowProjection.CountText(0)).IsEqualTo("0 Plugins");
    }

    // ===== Phase 8 评审 F3（Spec a.1）：可更新 warn tag + 更新按钮 + Description =====

    [Test]
    public async Task Row_UpdatablePlugin_WarnStatusAndUpdateAction()
    {
        PluginRow row = new(Sample[0], IsUpdatable: true);

        await Assert.That(row.StatusText).IsEqualTo("↻ 可更新");
        await Assert.That(row.StatusIsWarn).IsTrue();
        await Assert.That(row.StatusIsInfo).IsFalse();
        await Assert.That(row.ShowUpdate).IsTrue();
    }

    [Test]
    public async Task Row_DisabledUpdatablePlugin_DisabledWins()
    {
        // 状态 tag 优先级：已禁用 > 可更新 > 正常。
        PluginRow row = new(Sample[2], IsUpdatable: true);

        await Assert.That(row.StatusText).IsEqualTo("○ 已禁用");
        await Assert.That(row.StatusIsInfo).IsTrue();
        await Assert.That(row.StatusIsWarn).IsFalse();
        await Assert.That(row.ShowUpdate).IsFalse();
    }

    [Test]
    public async Task Row_NotUpdatable_NoWarnNoUpdate()
    {
        PluginRow row = new(Sample[0]);

        await Assert.That(row.IsUpdatable).IsFalse();
        await Assert.That(row.StatusIsWarn).IsFalse();
        await Assert.That(row.ShowUpdate).IsFalse();
        await Assert.That(row.StatusText).IsEqualTo("● 正常");
    }

    [Test]
    public async Task Filter_UpdatableNames_MarksMatchingRows()
    {
        IReadOnlyList<PluginRow> rows = PluginRowProjection.Filter(
            Sample, "", updatableNames: new HashSet<string> { "dsh-better-sidebar" });

        await Assert.That(rows[0].IsUpdatable).IsTrue();
        await Assert.That(rows[1].IsUpdatable).IsFalse();
        await Assert.That(rows[2].IsUpdatable).IsFalse();
    }

    [Test]
    public async Task Row_Description_ProjectsFromInfo()
    {
        PluginRow row = new(Sample[0]);

        await Assert.That(row.Description).IsEqualTo(Sample[0].Description);
    }
}
