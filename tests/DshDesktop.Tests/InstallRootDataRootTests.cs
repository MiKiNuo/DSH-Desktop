using DshDesktop.Infrastructure.Config;
using DshDesktop.Infrastructure.Updates;

namespace DshDesktop.Tests;

/// <summary>
/// 安装形态数据根解析测试（ADR-0009：数据根回迁安装根）。
/// 解析优先级：环境变量 DSH_DESKTOP_DATA_ROOT → 安装根（当前 exe 位于注册表 InstallLocation 下）
/// → %LOCALAPPDATA% 兜底。
/// 接缝：<see cref="DshDesktopConfigStore.ResolveDataRoot"/> 与
/// <see cref="InnoSetupInstallProbe.MatchRunningInstall"/> —— 纯函数（注册表取值由调用方注入），
/// 不触碰真实注册表与文件系统。
/// </summary>
public sealed class InstallRootDataRootTests
{
    private static readonly string DefaultRoot = @"C:\Users\test\AppData\Local\DshDesktop\data";

    [Test]
    public async Task ResolveDataRoot_InstalledAndExeUnderInstallRoot_ReturnsInstallDataDir()
    {
        string resolved = DshDesktopConfigStore.ResolveDataRoot(
            DefaultRoot,
            environmentOverride: null,
            installRoot: @"D:\Program Files\DSH-Desktop");

        await Assert.That(resolved).IsEqualTo(@"D:\Program Files\DSH-Desktop\data");
    }

    [Test]
    public async Task ResolveDataRoot_NotInstalled_FallsBackToDefault()
    {
        string resolved = DshDesktopConfigStore.ResolveDataRoot(
            DefaultRoot,
            environmentOverride: null,
            installRoot: null);

        await Assert.That(resolved).IsEqualTo(DefaultRoot);
    }

    [Test]
    public async Task ResolveDataRoot_EnvironmentOverride_AlwaysWins()
    {
        // 环境变量优先级最高：即使已安装也不随安装根。
        const string overridden = @"E:\Custom\DshData";
        string resolved = DshDesktopConfigStore.ResolveDataRoot(
            DefaultRoot,
            environmentOverride: overridden,
            installRoot: @"D:\Program Files\DSH-Desktop");

        await Assert.That(resolved).IsEqualTo(overridden);
    }

    [Test]
    public async Task MatchRunningInstall_ExeUnderInstallLocation_ReturnsInstallRoot()
    {
        // 真实临时目录（探测含 Directory.Exists 校验，路径必须存在；不用真实安装目录保持测试密闭）。
        string root = Path.Combine(Path.GetTempPath(), "dsh-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);

            string? matched = InnoSetupInstallProbe.MatchRunningInstall(
                registryInstallLocation: root + Path.DirectorySeparatorChar,
                exeDirectory: root);

            await Assert.That(matched).IsEqualTo(root + Path.DirectorySeparatorChar);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task MatchRunningInstall_ExeElsewhere_ReturnsNull()
    {
        // dotnet run / 便携解压：exe 不在安装根下，即使注册表有记录也不算安装形态
        // （否则开发机调试会把数据写进 Program Files——无 ACL 必然写失败）。
        string? matched = InnoSetupInstallProbe.MatchRunningInstall(
            registryInstallLocation: @"D:\Program Files\DSH-Desktop\",
            exeDirectory: @"F:\Dev\DSH-Desktop\src\DshDesktop.App\bin\Debug\net10.0-windows");

        await Assert.That(matched).IsNull();
    }

    [Test]
    public async Task MatchRunningInstall_NoRegistryRecord_ReturnsNull()
    {
        string? matched = InnoSetupInstallProbe.MatchRunningInstall(
            registryInstallLocation: null,
            exeDirectory: @"D:\Program Files\DSH-Desktop");

        await Assert.That(matched).IsNull();
    }

    [Test]
    public async Task MatchRunningInstall_SimilarPrefixSibling_NotConfused()
    {
        // 前缀陷阱：D:\Apps\DSH-Desktop-Portable 不是 D:\Apps\DSH-Desktop 的子路径。
        string? matched = InnoSetupInstallProbe.MatchRunningInstall(
            registryInstallLocation: @"D:\Apps\DSH-Desktop",
            exeDirectory: @"D:\Apps\DSH-Desktop-Portable");

        await Assert.That(matched).IsNull();
    }
}
