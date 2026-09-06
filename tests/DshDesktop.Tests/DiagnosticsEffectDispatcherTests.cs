using DshDesktop.Domain.Diagnostics;
using DshDesktop.Presentation.Avalonia.Features.Diagnostics;
using MiKiNuo.Mvi.Application.MVI.Mediator;
using MiKiNuo.Mvi.Application.MVI.Store;
using MiKiNuo.Mvi.Domain.MVI.Mediator;

namespace DshDesktop.Tests;

/// <summary>
/// Diagnostics 副作用分发器测试（Phase 8 Issue 06，§43.2：Effect → Mediator 路由链路；
/// zip 打包 / explorer / HTTP 探测在组合根执行，此处验证请求路由与失败回流为错误事件）。
/// </summary>
public sealed class DiagnosticsEffectDispatcherTests
{
    [Test]
    public async Task RunDiagnosis_RoutesRunDiagnosisRequest()
    {
        var mediator = new RecordingMediator();
        using var store = CreateStore(mediator);

        await store.DispatchAsync(new DiagnosticsIntent.RunDiagnosis());
        await WaitForAsync(() => mediator.Requests.Contains(nameof(RunDiagnosisRequest)));

        await Assert.That(mediator.Requests).Contains(nameof(RunDiagnosisRequest));
    }

    [Test]
    public async Task OpenLogsDirectory_RoutesOpenLogsDirectoryRequest()
    {
        var mediator = new RecordingMediator();
        using var store = CreateStore(mediator);

        await store.DispatchAsync(new DiagnosticsIntent.OpenLogsDirectory());
        await WaitForAsync(() => mediator.Requests.Contains(nameof(OpenLogsDirectoryRequest)));

        await Assert.That(mediator.Requests).Contains(nameof(OpenLogsDirectoryRequest));
    }

    [Test]
    public async Task ExportDiagnosticsBundle_RoutesDestinationPath()
    {
        var mediator = new RecordingMediator();
        using var store = CreateStore(mediator);

        await store.DispatchAsync(new DiagnosticsIntent.ExportDiagnosticsBundle(@"C:\Temp\diag.zip"));
        await WaitForAsync(() => mediator.ExportPaths.Count > 0);

        await Assert.That(mediator.ExportPaths[0]).IsEqualTo(@"C:\Temp\diag.zip");
    }

    [Test]
    public async Task MediatorFailure_AppendsErrorEventToEntries()
    {
        var mediator = new RecordingMediator { FailOnRequest = nameof(RunDiagnosisRequest) };
        using var store = CreateStore(mediator);

        await store.DispatchAsync(new DiagnosticsIntent.RunDiagnosis());
        await WaitForAsync(() => store.CurrentState.Entries.Count > 0);

        DiagnosticEvent entry = store.CurrentState.Entries[0];
        await Assert.That(entry.Level).IsEqualTo(DiagnosticLevel.Error);
        await Assert.That(entry.Source).IsEqualTo(DiagnosticSource.App);
        await Assert.That(entry.Message).Contains("导出失败占位");
    }

    private static MviStore<DiagnosticsState, DiagnosticsIntent, DiagnosticsEffect> CreateStore(
        RecordingMediator mediator)
    {
        return new MviStore<DiagnosticsState, DiagnosticsIntent, DiagnosticsEffect>(
            DiagnosticsState.Initial, new DiagnosticsReducer(), new DiagnosticsEffectDispatcher(mediator), []);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 300 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }
    }

    /// <summary>
    /// 表示按序记录请求的 Mediator 测试替身（记录导出目标路径载荷）。
    /// </summary>
    private sealed class RecordingMediator : IMviMediator
    {
        public List<string> Requests { get; } = [];

        public List<string> ExportPaths { get; } = [];

        public string? FailOnRequest { get; init; }

        /// <inheritdoc />
        public ValueTask<TResponse> SendAsync<TResponse>(
            IMviRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            string name = request.GetType().Name;
            Requests.Add(name);
            if (name == FailOnRequest)
            {
                throw new InvalidOperationException("导出失败占位");
            }

            object response = request switch
            {
                ExportDiagnosticsBundleRequest export => RecordExport(export),
                RunDiagnosisRequest or OpenLogsDirectoryRequest => true,
                _ => throw new NotSupportedException($"未登记的请求：{name}"),
            };
            return ValueTask.FromResult((TResponse)response);
        }

        private object RecordExport(ExportDiagnosticsBundleRequest request)
        {
            ExportPaths.Add(request.DestinationPath);
            return true;
        }
    }
}
