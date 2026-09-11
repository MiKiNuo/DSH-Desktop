using DshDesktop.Infrastructure.Config;

namespace DshDesktop.Tests;

/// <summary>
/// 数据根解析测试（ADR-0003 修订：数据根默认跟随安装盘，避免落系统盘 C 盘）。
/// 接缝：<see cref="DshDesktopConfigStore.ResolveDataRoot"/> —— 纯函数（入参注入环境变量取值器），
/// 不触碰真实文件系统与环境变量，故用例完全确定。
/// 解析优先级：环境变量 DSH_DESKTOP_DATA_ROOT → 安装根同级 data → 默认 %LOCALAPPDATA%。
/// </summary>
public sealed class DataRootResolutionTests
{
    private static readonly string DefaultRoot = @"C:\Users\test\AppData\Local\DshDesktop\data";
    private const string InstallRoot = @"D:\Program Files\DSH-Desktop";

    [Test]
    public async Task ResolveDataRoot_EnvironmentVariableSet_WinsOverInstallRoot()
    {
        const string overridden = @"E:\Custom\DshData";

        string resolved = DshDesktopConfigStore.ResolveDataRoot(
            DefaultRoot,
            InstallRoot,
            environmentOverride: overridden);

        await Assert.That(resolved).IsEqualTo(overridden);
    }

    [Test]
    public async Task ResolveDataRoot_InstalledOnNonSystemDrive_DefaultsToSiblingDataFolder()
    {
        // 需求：用户首次安装不要落 C 盘——安装根在 D 盘时，数据默认跟安装盘走。
        string resolved = DshDesktopConfigStore.ResolveDataRoot(
            DefaultRoot,
            InstallRoot,
            environmentOverride: null);

        await Assert.That(resolved).IsEqualTo(Path.Combine(InstallRoot, "data"));
    }

    [Test]
    public async Task ResolveDataRoot_NoInstallRoot_FallsBackToDefault()
    {
        // 未安装形态（dotnet run / 便携解压）：无安装根锚点，回退既有默认根，保持开发一致。
        string resolved = DshDesktopConfigStore.ResolveDataRoot(
            DefaultRoot,
            installRoot: null,
            environmentOverride: null);

        await Assert.That(resolved).IsEqualTo(DefaultRoot);
    }

    [Test]
    public async Task ResolveDataRoot_BlankEnvironmentOverride_Ignored()
    {
        // 空串/空白环境变量视为未设置：回退安装根同级 data，避免解析出空路径。
        string resolved = DshDesktopConfigStore.ResolveDataRoot(
            DefaultRoot,
            InstallRoot,
            environmentOverride: "   ");

        await Assert.That(resolved).IsEqualTo(Path.Combine(InstallRoot, "data"));
    }
}
