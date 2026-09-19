using DshDesktop.Infrastructure.Config;

namespace DshDesktop.Tests;

/// <summary>
/// 数据根解析测试——未安装形态（dotnet run / 便携解压）与优先级边界。
/// ADR-0009 起安装形态数据根随安装根（覆盖用例见 <see cref="InstallRootDataRootTests"/>）；
/// 本类锁兜底路径：环境变量 DSH_DESKTOP_DATA_ROOT → 默认 %LOCALAPPDATA%\DshDesktop\data。
/// 接缝：<see cref="DshDesktopConfigStore.ResolveDataRoot"/> —— 纯函数（入参注入环境变量取值），
/// 不触碰真实文件系统与环境变量，故用例完全确定。
/// </summary>
public sealed class DataRootResolutionTests
{
    private static readonly string DefaultRoot = @"C:\Users\test\AppData\Local\DshDesktop\data";

    [Test]
    public async Task ResolveDataRoot_EnvironmentVariableSet_Wins()
    {
        const string overridden = @"E:\Custom\DshData";

        string resolved = DshDesktopConfigStore.ResolveDataRoot(
            DefaultRoot,
            environmentOverride: overridden);

        await Assert.That(resolved).IsEqualTo(overridden);
    }

    [Test]
    public async Task ResolveDataRoot_NoOverride_FallsBackToDefault()
    {
        string resolved = DshDesktopConfigStore.ResolveDataRoot(
            DefaultRoot,
            environmentOverride: null);

        await Assert.That(resolved).IsEqualTo(DefaultRoot);
    }

    [Test]
    public async Task ResolveDataRoot_BlankEnvironmentOverride_Ignored()
    {
        // 空串/空白环境变量视为未设置：回退默认根，避免解析出空路径。
        string resolved = DshDesktopConfigStore.ResolveDataRoot(
            DefaultRoot,
            environmentOverride: "   ");

        await Assert.That(resolved).IsEqualTo(DefaultRoot);
    }
}
