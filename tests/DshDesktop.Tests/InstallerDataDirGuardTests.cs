namespace DshDesktop.Tests;

/// <summary>
/// 安装器数据目录守卫（ADR-0009）：数据根回迁安装根的前提是安装器预建 {app}\data
/// 并赋 users-modify ACL——缺了它，Program Files 下标准用户不可写，应用运行期所有
/// 落盘（config / logs / runtime / dsh-home）全部失败，比 ADR-0008 的 C 盘方案更糟。
/// App 项目不被测试项目引用，只能文本断言（锚点 CRLF 免疫）。
/// </summary>
public sealed class InstallerDataDirGuardTests
{
    [Test]
    public async Task Installer_PreCreatesDataDirWithUsersModifyAcl()
    {
        string? root = XamlScan.FindRepositoryRoot();
        await Assert.That(root).IsNotNull();

        string issPath = Path.Combine(root!, "installer", "DshDesktop.iss");
        string iss = (await File.ReadAllTextAsync(issPath)).Replace("\r\n", "\n");

        await Assert.That(iss.Contains("[Dirs]", StringComparison.Ordinal)).IsTrue();
        await Assert.That(iss.Contains("{app}\\data", StringComparison.Ordinal)).IsTrue();
        await Assert.That(iss.Contains("users-modify", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task DataRootResolution_ReferencesInstallProbe()
    {
        // 解析链守卫：DshDesktopConfigStore.DataRoot 必须经过 InnoSetupInstallProbe
        // （误删后数据根静默回退 %LOCALAPPDATA%，"数据随安装根"整体失守且无报错）。
        string? root = XamlScan.FindRepositoryRoot();
        await Assert.That(root).IsNotNull();

        string configPath = Path.Combine(
            root!, "src", "DshDesktop.Infrastructure", "Config", "DshDesktopConfig.cs");
        string source = (await File.ReadAllTextAsync(configPath)).Replace("\r\n", "\n");

        await Assert.That(
            source.Contains("InnoSetupInstallProbe.ProbeRunningInstallRoot()", StringComparison.Ordinal)).IsTrue();
    }
}
