using DshDesktop.Presentation.Avalonia.Features.AppShell;

namespace DshDesktop.Tests;

/// <summary>
/// 破坏性操作二次确认的文案与风险等级映射（视觉基准 docs/DSH-Desktop-UI-Redesign.html）。
/// 这三处操作在重设计前无任何确认：卸载插件、停止 DSH Runtime、切换 Runtime 版本。
/// </summary>
public sealed class ConfirmDialogTextTests
{
    [Test]
    public async Task Uninstall_And_Stop_AreFlaggedDangerous()
    {
        await Assert.That(ConfirmDialogText.IsDangerous(ConfirmAction.UninstallPlugin)).IsTrue();
        await Assert.That(ConfirmDialogText.IsDangerous(ConfirmAction.StopRuntime)).IsTrue();
    }

    [Test]
    public async Task Activate_IsNotDangerous()
    {
        // 切换 Runtime 失败会自动回退，属高影响但非破坏性，用主按钮而非危险按钮。
        await Assert.That(ConfirmDialogText.IsDangerous(ConfirmAction.ActivateRuntime)).IsFalse();
    }

    [Test]
    public async Task Every_Action_HasCompleteDialogText()
    {
        foreach (var action in Enum.GetValues<ConfirmAction>())
        {
            await Assert.That(ConfirmDialogText.Title(action)).IsNotEmpty();
            await Assert.That(ConfirmDialogText.Body(action)).IsNotEmpty();
            await Assert.That(ConfirmDialogText.ConfirmLabel(action)).IsNotEmpty();
        }
    }

    [Test]
    public async Task Titles_AreDistinct()
    {
        // 防复制粘贴事故：三个动作的标题不能雷同。
        var titles = Enum.GetValues<ConfirmAction>().Select(ConfirmDialogText.Title).ToList();
        await Assert.That(titles.Distinct().Count()).IsEqualTo(titles.Count);
    }

    [Test]
    public async Task Uninstall_Body_PromisesRollback()
    {
        // 卸载走插件事务，失败会回滚并重启原 Runtime——这是用户点确认前必须知道的信息。
        await Assert.That(ConfirmDialogText.Body(ConfirmAction.UninstallPlugin)).Contains("回滚");
    }

    [Test]
    public async Task StopRuntime_Body_PromisesSessionSurvives()
    {
        // 停止 Runtime 会断开工作台，但仅追加日志已落盘，会话内容不丢失。
        await Assert.That(ConfirmDialogText.Body(ConfirmAction.StopRuntime)).Contains("会话");
    }
}

/// <summary>
/// 确认通道的失败关闭语义。**注意**：本类只能有「未注册」这一条用例——通道是静态的，
/// 一旦有测试注册了 handler 就会污染同进程内的其他用例。
/// </summary>
public sealed class ConfirmDialogTests
{
    [Test]
    public async Task WithoutRegisteredHandler_FailsClosed()
    {
        // 壳未接线时不得放行破坏性操作。
        var confirmed = await ConfirmDialog.ShowAsync(ConfirmAction.StopRuntime, "PID 59028");
        await Assert.That(confirmed).IsFalse();
    }
}
