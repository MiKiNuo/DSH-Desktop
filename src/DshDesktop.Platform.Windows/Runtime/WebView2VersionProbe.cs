using Microsoft.Win32;

namespace DshDesktop.Platform.Windows.Runtime;

/// <summary>
/// 表示 WebView2 Runtime 版本探针（Phase 8 Issue 04：Runtime 页"运行环境"KV 卡数据源）：
/// 读 EdgeUpdate 注册表 client 键（{F3017226-...} 为 WebView2 Runtime 固定 GUID），
/// 不引用 WebView2 SDK、不起进程；未安装返回 null（UI 显示"未安装"）。
/// Phase 8 评审 F13：Windows 注册表探测是平台能力，层放置与 RunKeyStartupRegistrar 一致。
/// </summary>
public static class WebView2VersionProbe
{
    private const string ClientKeyPath =
        @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";

    /// <summary>
    /// 尝试读取已安装 WebView2 Runtime 的版本。
    /// </summary>
    /// <returns>版本文本（如 "149.0.0"）；未安装或不可读为 null。</returns>
    public static string? TryGetVersion()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        // 先查 HKLM（系统级安装），再查 HKCU（用户级安装）。
        foreach (RegistryHive hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            try
            {
                using RegistryKey? key = RegistryKey
                    .OpenBaseKey(hive, RegistryView.Default)
                    .OpenSubKey(ClientKeyPath);
                if (key?.GetValue("pv") is string { Length: > 0 } version)
                {
                    return version;
                }
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // 无权限/键不可读按未安装处理。
            }
        }

        return null;
    }
}
