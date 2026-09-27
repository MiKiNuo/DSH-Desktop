namespace DshDesktop.Tests;

/// <summary>
/// Runtime 页主操作按钮的接线守卫（用户症状 2026-09-28：运行环境页点「重启 Runtime」无反馈）。
///
/// 契约（View 层，XAML 编译期看不见、App 项目又不被测试项目引用，故按本仓库既定手法做源码文本守卫）：
/// ① 主操作按钮的文案取自 ViewModel 投影 → 状态变化时文案/可用性跟着变（非法状态禁用＝守卫可见）；
/// ② 命令只在 code-behind 接线：XAML 若再绑 <c>RestartRuntimeCommand</c>，绑定会覆盖包装命令，
///    Stopped 状态的点击会退回「CanExecute 恒真 + Reducer 忽略」的原始静默丢弃症状；
/// ③ Stopped 走既有 StartRuntime 链路；Running / Failed 重启前必须先过二次确认弹窗（与停止 DSH 对齐）。
/// </summary>
public sealed class RuntimeViewActionWiringTests
{
    [Test]
    public async Task PrimaryActionButton_TakesLabelFromViewModelProjection()
    {
        var text = await ReadViewAsync("RuntimeView.axaml");

        await Assert.That(text.Contains("x:Name=\"PrimaryActionButton\"", StringComparison.Ordinal)).IsTrue();
        await Assert.That(text.Contains("Content=\"{Binding PrimaryActionText}\"", StringComparison.Ordinal)).IsTrue();
        await Assert.That(text.Contains("Command=\"{Binding RestartRuntimeCommand}\"", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task PrimaryActionButton_RoutesStartDirectlyAndRestartThroughConfirm()
    {
        var code = await ReadViewAsync("RuntimeView.axaml.cs");

        // 包装命令存在（命令接线点），且 Stopped → 启动、Running/Failed → 先确认再重启。
        await Assert.That(code.Contains("RuntimePrimaryActionCommand", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("CanStartRuntime", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("StartRuntimeCommand", StringComparison.Ordinal)).IsTrue();
        await Assert.That(
            code.Contains("ConfirmDialog.ShowAsync(ConfirmAction.RestartRuntime", StringComparison.Ordinal)).IsTrue();
        // 停止 DSH 的既有确认不得被本次改动挤掉。
        await Assert.That(
            code.Contains("ConfirmDialog.ShowAsync(ConfirmAction.StopRuntime", StringComparison.Ordinal)).IsTrue();
    }

    private static async Task<string> ReadViewAsync(string fileName)
    {
        var root = XamlScan.FindRepositoryRoot();
        await Assert.That(root).IsNotNull();

        var path = Path.Combine(
            root!, "src", "DshDesktop.Presentation.Avalonia", "Features", "Runtime", fileName);
        await Assert.That(File.Exists(path)).IsTrue();
        return await File.ReadAllTextAsync(path);
    }
}
