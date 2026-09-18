using DshDesktop.Infrastructure.Config;

namespace DshDesktop.Tests;

/// <summary>
/// 数据根解析测试（Inno 安装形态修订：安装目录在 Program Files 下为管理员目录，
/// 运行期不可写，故数据根不再跟随安装盘）。
/// 接缝：<see cref="DshDesktopConfigStore.ResolveDataRoot"/> —— 纯函数（入参注入环境变量取值），
/// 不触碰真实文件系统与环境变量，故用例完全确定。
/// 解析优先级：环境变量 DSH_DESKTOP_DATA_ROOT → 默认 %LOCALAPPDATA%\DshDesktop\data。
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
