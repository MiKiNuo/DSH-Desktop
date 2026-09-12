using DshDesktop.Presentation.Avalonia.Features.Workbench;

namespace DshDesktop.Tests;

/// <summary>
/// Workbench 规约器测试（§21：DSH Web UI 视为黑盒）。
/// 用户精简掉页内工具条与错误条后，导航回流只推进 CurrentUrl / Loading；
/// 后退/前进/刷新/失败分支已随之整链删除（见 §21 Phase 6 修订注）。
/// </summary>
public sealed class WorkbenchReducerTests
{
    private readonly WorkbenchReducer _reducer = new();

    [Test]
    public async Task NavigationStarted_SetsUrlAndLoading()
    {
        WorkbenchState idle = WorkbenchState.Initial with { CurrentUrl = "http://127.0.0.1:0/" };

        var result = _reducer.Reduce(idle, new WorkbenchIntent.NavigationStarted("http://127.0.0.1:1/"));

        await Assert.That(result.State.CurrentUrl).IsEqualTo("http://127.0.0.1:1/");
        await Assert.That(result.State.Loading).IsTrue();
        await Assert.That(result.Effects.Count).IsEqualTo(0);
    }

    [Test]
    public async Task NavigationCompleted_StopsLoading()
    {
        WorkbenchState loading = WorkbenchState.Initial with { Loading = true };

        var result = _reducer.Reduce(
            loading,
            new WorkbenchIntent.NavigationCompleted("http://127.0.0.1:2/"));

        await Assert.That(result.State.CurrentUrl).IsEqualTo("http://127.0.0.1:2/");
        await Assert.That(result.State.Loading).IsFalse();
        await Assert.That(result.Effects.Count).IsEqualTo(0);
    }

    /// <summary>
    /// 导航失败不再有页内错误条承载，因此规约层只负责结束加载——
    /// 失败的可见性退回日志与诊断中心（用户明确接受的功能缩减）。
    /// 这条锁住「失败也会结束 Loading」，避免加载条永久悬停。
    /// </summary>
    [Test]
    public async Task NavigationCompleted_AfterFailure_StillStopsLoading()
    {
        WorkbenchState loading = WorkbenchState.Initial with
        {
            CurrentUrl = "http://127.0.0.1:4/",
            Loading = true,
        };

        var result = _reducer.Reduce(
            loading,
            new WorkbenchIntent.NavigationCompleted("http://127.0.0.1:4/"));

        await Assert.That(result.State.Loading).IsFalse();
        await Assert.That(result.State.CurrentUrl).IsEqualTo("http://127.0.0.1:4/");
    }
}
