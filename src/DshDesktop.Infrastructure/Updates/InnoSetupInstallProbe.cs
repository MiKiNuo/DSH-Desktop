using Microsoft.Win32;

namespace DshDesktop.Infrastructure.Updates;

/// <summary>
/// Inno Setup 安装形态探测（ADR-0009）：安装根 = 注册表 HKLM64 卸载键的 InstallLocation，
/// 但仅当当前运行的 exe 真位于其下才算数——开发机 dotnet run / 便携解压时注册表可能留有
/// 旧安装记录，exe 不在安装根下就跟随会把数据写进 Program Files（无 ACL 必然写失败）。
/// AppId 字面量单源在 <see cref="GitHubDesktopUpdater"/>（文本守卫
/// InstallerAppId_MatchesUpdaterRegistryProbe 锁定与 installer/DshDesktop.iss 一致）。
/// internal 供 DshDesktop.Tests 直测（InternalsVisibleTo）。
/// </summary>
internal static class InnoSetupInstallProbe
{
    /// <summary>
    /// 探测当前进程的安装根：注册表 InstallLocation 与 exe 所在目录双重命中才返回。
    /// </summary>
    /// <returns>安装根绝对路径；未安装或 exe 不在安装根下为 null。</returns>
    internal static string? ProbeRunningInstallRoot()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        return MatchRunningInstall(ProbeRegistryInstallLocation(), AppContext.BaseDirectory);
    }

    /// <summary>
    /// 判定注册表安装位置与 exe 所在目录是否构成"安装形态运行"。纯函数，供直测。
    /// </summary>
    /// <param name="registryInstallLocation">注册表 InstallLocation 取值（null = 未安装）。</param>
    /// <param name="exeDirectory">当前 exe 所在目录（AppContext.BaseDirectory）。</param>
    /// <returns>安装根（原样返回注册表取值）；不构成安装形态为 null。</returns>
    internal static string? MatchRunningInstall(string? registryInstallLocation, string exeDirectory)
    {
        if (string.IsNullOrWhiteSpace(registryInstallLocation) || !Directory.Exists(registryInstallLocation))
        {
            return null;
        }

        // 前缀陷阱防御：DSH-Desktop-Portable 不是 DSH-Desktop 的子路径——两侧都补尾部分隔符再比
        // （exeDirectory 可能恰等于安装根本身而无尾部分隔符）。
        string rootWithSeparator = registryInstallLocation.EndsWith(Path.DirectorySeparatorChar)
            ? registryInstallLocation
            : registryInstallLocation + Path.DirectorySeparatorChar;
        string exeWithSeparator = exeDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? exeDirectory
            : exeDirectory + Path.DirectorySeparatorChar;
        return exeWithSeparator.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
            ? registryInstallLocation
            : null;
    }

    /// <summary>
    /// 读注册表 InstallLocation（HKLM64 卸载键）。任何异常（权限 / 损坏）一律按未安装处理。
    /// </summary>
    private static string? ProbeRegistryInstallLocation()
    {
        try
        {
            using RegistryKey? key = RegistryKey
                .OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .OpenSubKey(GitHubDesktopUpdater.UninstallSubKey);
            return key?.GetValue("InstallLocation") as string;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
