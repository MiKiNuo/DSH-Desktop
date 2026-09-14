using System.Diagnostics;
using DshDesktop.Application.Runtime;
using DshDesktop.Infrastructure.Runtime;

namespace DshDesktop.Tests;

/// <summary>
/// ② harness 启动环境的 PATH 前置（2026-09-15 定位的闸门）：dsh-market 的 probePnpm() 与它
/// 拉起的 dsh CLI 都在【harness 进程的 PATH】上按名字找 pnpm（spawnEnv = process.env.PATH +
/// node 目录 + PNPM_HOME）。垫片目录不进 harness PATH，市场就永远看不到 pnpm —— 横幅常驻、装不了。
/// 顺带补齐上游 harness env 的三个平价变量（NO_COLOR / pnpm side-effects cache）。
/// </summary>
public sealed class DshProcessHostStartInfoTests
{
    private const string ToolBin = @"C:\data\dsh-home\.desktop-bin";

    private static RuntimeLaunchOptions Options(string? toolBinDirectory) => new(
        NodePath: @"C:\tool\node.exe",
        EntryPath: @"C:\dsh\lib\bin.js",
        HarnessNodeEntryPath: @"C:\app\resources\harness-node-entry.mjs",
        ToolBinDirectory: toolBinDirectory,
        WorkingDirectory: @"C:\dsh",
        DshHome: @"C:\data\dsh-home",
        Host: "127.0.0.1",
        Port: 0,
        StartupTimeout: TimeSpan.FromSeconds(60));

    /// <summary>垫片目录与 node 所在目录必须前置到 PATH（顺序：垫片在前），并保留原有 PATH。</summary>
    [Test]
    public async Task BuildStartInfo_PrependsToolBinAndNodeDirectoryToPath()
    {
        ProcessStartInfo info = DshProcessHost.BuildStartInfo(Options(ToolBin), 4321);

        string[] parts = info.Environment["Path"]!.Split(Path.PathSeparator);
        await Assert.That(parts[0]).IsEqualTo(ToolBin);
        await Assert.That(parts[1]).IsEqualTo(@"C:\tool");
        await Assert.That(info.Environment["Path"]!.EndsWith(Environment.GetEnvironmentVariable("Path") ?? string.Empty)).IsTrue();
        await Assert.That(info.Environment["DSH_HOME"]).IsEqualTo(@"C:\data\dsh-home");
    }

    /// <summary>上游平价：harness 也带这三个变量，否则市场侧 pnpm 会走慢路径/输出带色码。</summary>
    [Test]
    public async Task BuildStartInfo_SetsPnpmParityEnvironmentVariables()
    {
        ProcessStartInfo info = DshProcessHost.BuildStartInfo(Options(ToolBin), 4321);

        await Assert.That(info.Environment["NO_COLOR"]).IsEqualTo("1");
        await Assert.That(info.Environment["npm_config_side_effects_cache"]).IsEqualTo("false");
        await Assert.That(info.Environment["PNPM_CONFIG_SIDE_EFFECTS_CACHE"]).IsEqualTo("false");
    }

    /// <summary>未配置垫片（null）时 PATH 逐字不动：旧配置/旧数据根不得回归。</summary>
    [Test]
    public async Task BuildStartInfo_NoToolBinDirectory_LeavesPathUntouched()
    {
        ProcessStartInfo info = DshProcessHost.BuildStartInfo(Options(null), 4321);

        await Assert.That(info.Environment["Path"]).IsEqualTo(Environment.GetEnvironmentVariable("Path"));
    }

    /// <summary>垫片目录与 node 目录相同时只出现一次（不因重复追加而膨胀 PATH）。</summary>
    [Test]
    public async Task BuildStartInfo_SameDirectory_AppearsOnce()
    {
        ProcessStartInfo info = DshProcessHost.BuildStartInfo(Options(@"C:\tool"), 4321);

        int occurrences = info.Environment["Path"]!.Split(Path.PathSeparator).Count(part => part == @"C:\tool");
        await Assert.That(occurrences).IsEqualTo(1);
    }

    /// <summary>NodePath 可为 PATH 上的裸名（配置注释允许）：此时不注入空路径段。</summary>
    [Test]
    public async Task BuildStartInfo_BareNodeName_AddsNoEmptySegment()
    {
        ProcessStartInfo info = DshProcessHost.BuildStartInfo(Options(ToolBin) with { NodePath = "node" }, 4321);

        await Assert.That(info.Environment["Path"]!.Split(Path.PathSeparator).Any(part => part.Length == 0)).IsFalse();
    }
}
