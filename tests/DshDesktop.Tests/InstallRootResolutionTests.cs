using DshDesktop.Infrastructure.Config;

namespace DshDesktop.Tests;

/// <summary>
/// 安装根探测测试（ADR-0003 修订）：
/// Velopack 安装形态下 exe 位于 &lt;安装根&gt;\current\，须上跳一级得到安装根；
/// 非安装形态（开发 bin 目录、便携解压）返回 null 以回退默认数据根。
/// 接缝：<see cref="DshDesktopConfigStore.ResolveInstallRoot"/>（baseDirectory 注入，无环境依赖）。
/// </summary>
public sealed class InstallRootResolutionTests
{
    [Test]
    public async Task ResolveInstallRoot_VelopackCurrentLayout_ReturnsInstallRoot()
    {
        // 实测形态：D:\Program Files\DSH-Desktop\current\DshDesktop.App.exe
        string? root = DshDesktopConfigStore.ResolveInstallRoot(
            @"D:\Program Files\DSH-Desktop\current\");

        await Assert.That(root).IsEqualTo(@"D:\Program Files\DSH-Desktop");
    }

    [Test]
    public async Task ResolveInstallRoot_CurrentLayoutWithoutTrailingSeparator_ReturnsInstallRoot()
    {
        string? root = DshDesktopConfigStore.ResolveInstallRoot(
            @"D:\Program Files\DSH-Desktop\current");

        await Assert.That(root).IsEqualTo(@"D:\Program Files\DSH-Desktop");
    }

    [Test]
    public async Task ResolveInstallRoot_CurrentLayoutCaseInsensitive_ReturnsInstallRoot()
    {
        string? root = DshDesktopConfigStore.ResolveInstallRoot(@"C:\App\CURRENT\");

        await Assert.That(root).IsEqualTo(@"C:\App");
    }

    [Test]
    public async Task ResolveInstallRoot_DevelopmentBinLayout_ReturnsNull()
    {
        // 开发形态：dotnet run 时 exe 在 bin\Debug\net10.0-windows\，非 current，应回退默认根。
        string? root = DshDesktopConfigStore.ResolveInstallRoot(
            @"F:\MiKiNuoProjects\DSH-Desktop\src\DshDesktop.App\bin\Debug\net10.0-windows\");

        await Assert.That(root).IsNull();
    }

    [Test]
    public async Task ResolveInstallRoot_RootedPath_ReturnsNull()
    {
        // 边界：BaseDirectory 直接就是盘根时不应越界或抛错。
        string? root = DshDesktopConfigStore.ResolveInstallRoot(@"C:\");

        await Assert.That(root).IsNull();
    }
}
