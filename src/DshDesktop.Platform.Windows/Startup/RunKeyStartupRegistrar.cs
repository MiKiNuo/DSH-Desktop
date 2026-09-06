using Microsoft.Win32;
using DshDesktop.Application.Startup;

namespace DshDesktop.Platform.Windows.Startup;

/// <summary>
/// 表示 <see cref="IStartupRegistrar"/> 的 Windows 注册表实现（Phase 8 Issue 05）：
/// HKCU\Software\Microsoft\Windows\CurrentVersion\Run 写/删 "DshDesktop" 值。
/// </summary>
public sealed class RunKeyStartupRegistrar : IStartupRegistrar
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DshDesktop";

    /// <inheritdoc />
    public void SetEnabled(bool enabled, string executablePath)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
        {
            key.SetValue(ValueName, $"\"{executablePath}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
