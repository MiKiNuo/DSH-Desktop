using DshDesktop.Infrastructure.Config;

namespace DshDesktop.Tests;

/// <summary>
/// 配置目录创建测试（首次安装缺陷回归）：
/// 全新安装形态下数据根内没有 config 目录，SaveAsync 的 File.Create 不会建父目录，
/// 导致 DirectoryNotFoundException 中断启动。此测试锁定"保存前先建目录"的行为。
/// 接缝：<see cref="DshDesktopConfigStore.EnsureConfigDirectory"/>（路径注入，无静态环境依赖）。
/// </summary>
public sealed class ConfigSaveDirectoryTests
{
    [Test]
    public async Task EnsureConfigDirectory_MissingNestedParents_CreatesThem()
    {
        string root = Path.Combine(Path.GetTempPath(), "dsh-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            string configPath = Path.Combine(root, "data", "config", "dsh-desktop.config.json");
            string parent = Path.GetDirectoryName(configPath)!;
            await Assert.That(Directory.Exists(parent)).IsFalse();

            DshDesktopConfigStore.EnsureConfigDirectory(configPath);

            await Assert.That(Directory.Exists(parent)).IsTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task EnsureConfigDirectory_AlreadyExists_IsIdempotent()
    {
        string root = Path.Combine(Path.GetTempPath(), "dsh-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            string configPath = Path.Combine(root, "data", "config", "dsh-desktop.config.json");
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

            DshDesktopConfigStore.EnsureConfigDirectory(configPath);
            DshDesktopConfigStore.EnsureConfigDirectory(configPath);

            await Assert.That(Directory.Exists(Path.GetDirectoryName(configPath)!)).IsTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task SaveAsync_ConfigDirectoryMissing_CreatesFileIncludingParents()
    {
        // 端到端：模拟全新安装——目录树完全不存在时保存配置不应抛异常。
        string root = Path.Combine(Path.GetTempPath(), "dsh-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            string configPath = Path.Combine(root, "data", "config", "dsh-desktop.config.json");

            await DshDesktopConfigStore.SaveToPathAsync(new DshDesktopConfig { DshChannel = "alpha" }, configPath);

            await Assert.That(File.Exists(configPath)).IsTrue();
            string json = await File.ReadAllTextAsync(configPath);
            await Assert.That(json).Contains("\"dshChannel\": \"alpha\"");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
