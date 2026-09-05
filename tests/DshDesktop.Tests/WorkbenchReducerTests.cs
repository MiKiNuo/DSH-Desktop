using DshDesktop.Presentation.Avalonia.Features.Workbench;

namespace DshDesktop.Tests;

/// <summary>
/// Workbench 规约器测试（§21：DSH Web UI 视为黑盒；导航事件回流推进 Loading/Error/CanGoBack/CanGoForward）。
/// </summary>
public sealed class WorkbenchReducerTests
{
    private readonly WorkbenchReducer _reducer = new();

    [Test]
    public async Task NavigationStarted_SetsUrlAndLoadingAndClearsError()
    {
        WorkbenchState failed = WorkbenchState.Initial with { Error = "旧错误" };

        var result = _reducer.Reduce(failed, new WorkbenchIntent.NavigationStarted("http://127.0.0.1:1/"));

        await Assert.That(result.State.CurrentUrl).IsEqualTo("http://127.0.0.1:1/");
        await Assert.That(result.State.Loading).IsTrue();
        await Assert.That(result.State.Error).IsNull();
    }

    [Test]
    public async Task NavigationCompleted_StopsLoadingAndAdvancesHistoryFlags()
    {
        WorkbenchState loading = WorkbenchState.Initial with { Loading = true };

        var result = _reducer.Reduce(
            loading,
            new WorkbenchIntent.NavigationCompleted("http://127.0.0.1:2/", CanGoBack: true, CanGoForward: false));

        await Assert.That(result.State.CurrentUrl).IsEqualTo("http://127.0.0.1:2/");
        await Assert.That(result.State.Loading).IsFalse();
        await Assert.That(result.State.CanGoBack).IsTrue();
        await Assert.That(result.State.CanGoForward).IsFalse();
    }

    [Test]
    public async Task NavigationCompleted_ClearsPreviousError()
    {
        WorkbenchState failed = WorkbenchState.Initial with { Loading = true, Error = "旧错误" };

        var result = _reducer.Reduce(
            failed,
            new WorkbenchIntent.NavigationCompleted("http://127.0.0.1:3/", CanGoBack: false, CanGoForward: true));

        await Assert.That(result.State.Error).IsNull();
        await Assert.That(result.State.CanGoForward).IsTrue();
    }

    [Test]
    public async Task NavigationFailed_StopsLoadingAndSetsError()
    {
        WorkbenchState loading = WorkbenchState.Initial with
        {
            CurrentUrl = "http://127.0.0.1:4/",
            Loading = true,
        };

        var result = _reducer.Reduce(loading, new WorkbenchIntent.NavigationFailed("连接失败"));

        await Assert.That(result.State.Loading).IsFalse();
        await Assert.That(result.State.Error).IsEqualTo("连接失败");
        await Assert.That(result.State.CurrentUrl).IsEqualTo("http://127.0.0.1:4/");
        await Assert.That(result.Effects.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Reload_SetsLoadingAndClearsError()
    {
        WorkbenchState failed = WorkbenchState.Initial with
        {
            CurrentUrl = "http://127.0.0.1:5/",
            Error = "旧错误",
        };

        var result = _reducer.Reduce(failed, new WorkbenchIntent.Reload());

        await Assert.That(result.State.Loading).IsTrue();
        await Assert.That(result.State.Error).IsNull();
        await Assert.That(result.State.CurrentUrl).IsEqualTo("http://127.0.0.1:5/");
        await Assert.That(result.Effects.Count).IsEqualTo(0);
    }

    [Test]
    public async Task NavigateBack_SetsLoading()
    {
        var result = _reducer.Reduce(WorkbenchState.Initial, new WorkbenchIntent.NavigateBack());

        await Assert.That(result.State.Loading).IsTrue();
        await Assert.That(result.State.Error).IsNull();
    }

    [Test]
    public async Task NavigateForward_SetsLoading()
    {
        var result = _reducer.Reduce(WorkbenchState.Initial, new WorkbenchIntent.NavigateForward());

        await Assert.That(result.State.Loading).IsTrue();
        await Assert.That(result.State.Error).IsNull();
    }
}
