using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DshDesktop.Application.Runtime;

namespace DshDesktop.Infrastructure.Runtime;

/// <summary>
/// 表示 DSH Runtime 进程宿主：拉起 / 就绪等待 / 停止（§4.4 适配器，§16 真实 OS 状态来源）。
/// </summary>
/// <remarks>
/// 就绪判定复刻 Electron 壳（docs/DSH-Launch-Mechanics.md §4）：
/// stdout 行 `dsh web: &lt;url&gt;?token=...` 捕获完整地址 → HTTP 轮询至状态码 ∈ [200,500) 连续稳定。
/// 停止策略见 ADR-0002：Windows 上直接结束进程树。
/// 退出事件语义：自发退出（崩溃）与主动停止都会触发 <see cref="Exited"/>；
/// 启动失败时的清理性终止不触发（失败已由 StartAsync 的异常表达）。
/// </remarks>
public sealed partial class DshProcessHost : IRuntimeOrchestrator
{
    private const int ReadyPollIntervalMs = 300;
    private const int ReadyStableSuccesses = 2;
    private const int StopWaitMs = 10_000;

    private readonly HttpClient _httpClient = new();
    private readonly StringBuilder _stderrTail = new();
    private readonly object _sync = new();

    private Launch? _launch;

    /// <inheritdoc />
    public event EventHandler<RuntimeExitedEventArgs>? Exited;

    /// <summary>
    /// DSH 进程每输出一行（stdout / stderr）时触发（§24 Diagnostics 事件源）。
    /// </summary>
    public event EventHandler<ProcessOutputLineEventArgs>? OutputReceived;

    /// <inheritdoc />
    public async Task<RuntimeStartResult> StartAsync(
        RuntimeLaunchOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);

        lock (_sync)
        {
            if (_launch is { Process.HasExited: false })
            {
                throw new InvalidOperationException("Runtime 已在运行，不能重复启动。");
            }
        }

        int port = options.Port > 0 ? options.Port : PortProbe.FindFreeTcpPort(options.Host);
        Process process = new()
        {
            StartInfo = BuildStartInfo(options, port),
            EnableRaisingEvents = true,
        };

        Launch launch = new()
        {
            Process = process,
            ReadyUrl = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously),
        };

        lock (_sync)
        {
            _stderrTail.Clear();
            _launch = launch;
        }

        process.Exited += OnProcessExited;
        process.OutputDataReceived += (_, args) =>
        {
            TryCaptureReadyUrl(args.Data, launch);
            if (args.Data is { } stdoutLine)
            {
                OutputReceived?.Invoke(this, new ProcessOutputLineEventArgs(false, stdoutLine));
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            AppendStderr(args.Data);
            if (args.Data is { } stderrLine)
            {
                OutputReceived?.Invoke(this, new ProcessOutputLineEventArgs(true, stderrLine));
            }
        };

        options.Progress?.Report(RuntimeStartupSignal.Spawning);

        try
        {
            process.Start();
        }
        catch (Exception exception)
        {
            launch.SuppressExitEvent = true;
            CleanupLaunch(launch);
            throw new InvalidOperationException(
                $"无法启动 DSH 进程（{options.NodePath}）：{exception.Message}", exception);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        options.Progress?.Report(RuntimeStartupSignal.WaitingReady);

        using CancellationTokenSource timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(options.StartupTimeout);

        try
        {
            string url = await launch.ReadyUrl.Task
                .WaitAsync(timeoutSource.Token)
                .ConfigureAwait(false);

            // stdout 就绪行 = DSH / Profile / Plugins 就绪；此后仅剩 HTTP 探测（Phase 8 Issue 03 计时标记）。
            options.Progress?.Report(RuntimeStartupSignal.HttpProbing);

            await WaitHttpReadyAsync(url, timeoutSource.Token).ConfigureAwait(false);
            return new RuntimeStartResult(process.Id, port, url);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            launch.SuppressExitEvent = true;
            StopLaunch(launch);
            throw new TimeoutException(
                $"DSH 启动超过 {options.StartupTimeout.TotalSeconds:0}s 未就绪。{ReadStderrTail()}");
        }
        catch
        {
            launch.SuppressExitEvent = true;
            StopLaunch(launch);
            throw;
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        Launch? launch;
        lock (_sync)
        {
            launch = _launch;
        }

        if (launch is not null)
        {
            StopLaunch(launch);
        }

        return Task.CompletedTask;
    }

    private static void ValidateOptions(RuntimeLaunchOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.NodePath) || string.IsNullOrWhiteSpace(options.EntryPath))
        {
            throw new InvalidOperationException(
                "Runtime 路径未配置（NodePath / EntryPath 为空）。请检查 exe 旁 dsh-desktop.config.json。");
        }

        if (!File.Exists(options.EntryPath))
        {
            throw new InvalidOperationException(
                $"找不到 DSH 入口：{options.EntryPath}。请检查 dsh-desktop.config.json 的 dshEntryPath。");
        }
    }

    /// <summary>
    /// 组装启动参数。internal 供 DshDesktop.Tests 直测（InternalsVisibleTo）：PATH 前置与环境变量
    /// 是「工作台内插件市场能否安装」的判定点，必须在测试里锁住。
    /// </summary>
    internal static ProcessStartInfo BuildStartInfo(RuntimeLaunchOptions options, int port)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = options.NodePath,
            WorkingDirectory = string.IsNullOrWhiteSpace(options.WorkingDirectory)
                ? Path.GetDirectoryName(options.EntryPath) ?? Environment.CurrentDirectory
                : options.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        if (!string.IsNullOrWhiteSpace(options.HarnessNodeEntryPath))
        {
            startInfo.ArgumentList.Add("--expose-internals");
            startInfo.ArgumentList.Add(options.HarnessNodeEntryPath);
        }

        startInfo.ArgumentList.Add(options.EntryPath);
        startInfo.ArgumentList.Add("web");
        startInfo.ArgumentList.Add("--no-open");
        startInfo.ArgumentList.Add("--host");
        startInfo.ArgumentList.Add(options.Host);
        startInfo.ArgumentList.Add("--port");
        startInfo.ArgumentList.Add(port.ToString(CultureInfo.InvariantCulture));

        startInfo.Environment["DSH_HOME"] = options.DshHome;

        // 上游 Electron 壳的 harness env 平价项（buildHarnessSpawnOptions）：
        // NO_COLOR 免 DSH 子进程输出色码，两个 side-effects-cache 关掉 pnpm 的副作用缓存
        // （上游注释记录：相关开关配错会让 install 变成跨盘全量拷贝，几分钟到半小时）。
        startInfo.Environment["NO_COLOR"] = "1";
        startInfo.Environment["npm_config_side_effects_cache"] = "false";
        startInfo.Environment["PNPM_CONFIG_SIDE_EFFECTS_CACHE"] = "false";

        PrependToolPath(startInfo, options);
        return startInfo;
    }

    /// <summary>
    /// 把工具垫片目录与 node 所在目录前置进 PATH（顺序：垫片在前），去重、不注入空段。
    /// </summary>
    /// <remarks>
    /// 工作台内的 dsh-market 与其拉起的 dsh CLI 按【名字】调用 pnpm（probePnpm 亦同），
    /// 搜索范围就是 harness 进程的 PATH ⇒ 垫片目录必须在这里。NodePath 允许是 PATH 上的裸名，
    /// 此时 Path.GetDirectoryName 返回空串，跳过即可。
    /// </remarks>
    private static void PrependToolPath(ProcessStartInfo startInfo, RuntimeLaunchOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ToolBinDirectory))
        {
            return;
        }

        char separator = Path.PathSeparator;
        string current = startInfo.Environment.TryGetValue("Path", out string? existing) && existing is not null
            ? existing
            : string.Empty;
        List<string> parts = [.. current.Split(separator).Where(part => part.Length > 0)];

        List<string> additions = [];
        foreach (string? directory in new[] { options.ToolBinDirectory, Path.GetDirectoryName(options.NodePath) })
        {
            if (string.IsNullOrWhiteSpace(directory) || parts.Contains(directory) || additions.Contains(directory))
            {
                continue;
            }

            additions.Add(directory);
        }

        if (additions.Count == 0)
        {
            return;
        }

        parts.InsertRange(0, additions);
        startInfo.Environment["Path"] = string.Join(separator, parts);
    }

    private void TryCaptureReadyUrl(string? line, Launch launch)
    {
        if (line is null)
        {
            return;
        }

        Match match = ReadyUrlRegex().Match(line);
        if (match.Success)
        {
            launch.ReadyUrl.TrySetResult(match.Groups[1].Value);
        }
    }

    private void AppendStderr(string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (_sync)
        {
            if (_stderrTail.Length > 4096)
            {
                _stderrTail.Remove(0, _stderrTail.Length - 4096);
            }

            _stderrTail.AppendLine(line);
        }
    }

    private string ReadStderrTail()
    {
        lock (_sync)
        {
            string tail = _stderrTail.ToString().Trim();
            return tail.Length == 0 ? string.Empty : $"stderr 末尾：{tail}";
        }
    }

    private async Task WaitHttpReadyAsync(string url, CancellationToken cancellationToken)
    {
        int consecutiveSuccesses = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using HttpResponseMessage response = await _httpClient
                    .GetAsync(url, cancellationToken)
                    .ConfigureAwait(false);
                if ((int)response.StatusCode < 500)
                {
                    consecutiveSuccesses++;
                    if (consecutiveSuccesses >= ReadyStableSuccesses)
                    {
                        return;
                    }
                }
                else
                {
                    consecutiveSuccesses = 0;
                }
            }
            catch (HttpRequestException)
            {
                consecutiveSuccesses = 0;
            }

            await Task.Delay(ReadyPollIntervalMs, cancellationToken).ConfigureAwait(false);
        }
    }

    private void OnProcessExited(object? sender, EventArgs args)
    {
        Launch? launch;
        lock (_sync)
        {
            launch = _launch;
        }

        if (launch is null || !ReferenceEquals(sender, launch.Process))
        {
            return;
        }

        int? exitCode = TryGetExitCode(launch.Process);
        launch.ReadyUrl.TrySetException(new InvalidOperationException(
            $"DSH 进程在就绪前退出（退出码 {exitCode?.ToString(CultureInfo.InvariantCulture) ?? "未知"}）。{ReadStderrTail()}"));

        RaiseExitedOnce(launch, exitCode);
    }

    private void RaiseExitedOnce(Launch launch, int? exitCode)
    {
        if (launch.SuppressExitEvent)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref launch.ExitEventRaised, 1, 0) == 0)
        {
            Exited?.Invoke(this, new RuntimeExitedEventArgs(exitCode));
        }
    }

    private void StopLaunch(Launch launch)
    {
        try
        {
            if (!launch.Process.HasExited)
            {
                launch.Process.Kill(entireProcessTree: true);
                launch.Process.WaitForExit(StopWaitMs);
            }
        }
        catch (InvalidOperationException)
        {
            // 进程已退出。
        }

        // 主动停止也回报退出（ExitCode 可能因事件竞态未投递，这里兜底恰好一次）。
        RaiseExitedOnce(launch, TryGetExitCode(launch.Process));
        CleanupLaunch(launch);
    }

    private void CleanupLaunch(Launch launch)
    {
        lock (_sync)
        {
            if (ReferenceEquals(_launch, launch))
            {
                _launch = null;
            }
        }

        launch.Process.Exited -= OnProcessExited;
        launch.Process.Dispose();
    }

    private static int? TryGetExitCode(Process process)
    {
        try
        {
            return process.HasExited ? process.ExitCode : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// 匹配 stdout 中的就绪行（AOT 友好的源生成正则）。
    /// </summary>
    [GeneratedRegex(@"dsh web:\s*(https?://\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex ReadyUrlRegex();

    /// <summary>
    /// 表示一次 Runtime 拉起的完整上下文（进程代际）。
    /// </summary>
    private sealed class Launch
    {
        /// <summary>DSH 进程。</summary>
        public required Process Process { get; init; }

        /// <summary>就绪 URL（含 token）的等待源。</summary>
        public required TaskCompletionSource<string> ReadyUrl { get; init; }

        /// <summary>是否抑制退出事件（启动失败的清理性终止为 true）。</summary>
        public bool SuppressExitEvent { get; set; }

        /// <summary>退出事件恰好一次的标记（Interlocked）。</summary>
        public int ExitEventRaised;
    }
}
