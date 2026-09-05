using DshDesktop.Presentation.Avalonia.Features.Workbench;

namespace DshDesktop.Tests;

/// <summary>
/// Workbench 规约器测试（§21：DSH Web UI 视为黑盒，只跟踪导航地址）。
/// </summary>
public sealed class WorkbenchReducerTests
{
    private readonly WorkbenchReducer _reducer = new();

    [Test]
    public async Task NavigationStarted_SetsCurrentUrl()
    {
        var result = _reducer.Reduce(WorkbenchState.Initial, new WorkbenchIntent.NavigationStarted("http://127.0.0.1:1/"));

        await Assert.That(result.State.CurrentUrl).IsEqualTo("http://127.0.0.1:1/");
    }

    [Test]
    public async Task NavigationCompleted_SetsCurrentUrl()
    {
        var result = _reducer.Reduce(WorkbenchState.Initial, new WorkbenchIntent.NavigationCompleted("http://127.0.0.1:2/"));

        await Assert.That(result.State.CurrentUrl).IsEqualTo("http://127.0.0.1:2/");
    }

    [Test]
    public async Task NavigationFailed_KeepsStateUnchanged()
    {
        WorkbenchState navigated = WorkbenchState.Initial with { CurrentUrl = "http://127.0.0.1:3/" };

        var result = _reducer.Reduce(navigated, new WorkbenchIntent.NavigationFailed("连接失败"));

        await Assert.That(result.State.CurrentUrl).IsEqualTo("http://127.0.0.1:3/");
        await Assert.That(result.Effects.Count).IsEqualTo(0);
    }
}
