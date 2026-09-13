using System.Text.Json;
using DshDesktop.Infrastructure.Config;

namespace DshDesktop.Tests;

/// <summary>
/// 配置写入原子性与容错读取测试（2026-09-13 Runtime 启动失败回归）。
///
/// 背景：<c>SaveToPathAsync</c> 原用 <c>File.Create</c>，该调用**立即把目标文件截断为 0 字节**，
/// 之后才逐块序列化写入。窗口期内（或写入被并发实例打断时）磁盘上就是合法的「0 字节」形态。
/// 而 <c>LoadOrDetectAsync</c> 只判 <c>File.Exists</c>，对空文件直接反序列化 → JsonException
/// → BootstrapRuntimeAsync 的 catch 只记日志 → Runtime 初始化被静默跳过，表现为「窗口能开、
/// Runtime 永不起来」。
///
/// 接缝：<see cref="DshDesktopConfigStore.SaveToPathAsync"/> /
/// <see cref="DshDesktopConfigStore.LoadFromPathAsync"/>（均路径注入，无静态环境依赖）。
/// </summary>
public sealed class ConfigAtomicWriteTests
{
    [Test]
    public async Task SaveToPathAsync_LeavesNoTempFileBehind()
    {
        // 原子写的副产品：临时文件必须被 Move 掉，不得残留（残留会污染 config 目录）。
        string root = NewTempDir();
        try
        {
            string configPath = Path.Combine(root, "config", "dsh-desktop.config.json");

            await DshDesktopConfigStore.SaveToPathAsync(new DshDesktopConfig { DshChannel = "alpha" }, configPath);

            string[] leftovers = Directory.GetFiles(Path.GetDirectoryName(configPath)!);
            await Assert.That(leftovers).Count().IsEqualTo(1);
            await Assert.That(leftovers[0]).IsEqualTo(configPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task SaveToPathAsync_OverwritesExistingConfig()
    {
        // 覆盖既有文件（正常路径，非首次安装）：内容必须是新值，且解析可用。
        string root = NewTempDir();
        try
        {
            string configPath = Path.Combine(root, "config", "dsh-desktop.config.json");
            await DshDesktopConfigStore.SaveToPathAsync(new DshDesktopConfig { DshChannel = "latest" }, configPath);

            await DshDesktopConfigStore.SaveToPathAsync(new DshDesktopConfig { DshChannel = "alpha" }, configPath);

            DshDesktopConfig? reloaded = await DshDesktopConfigStore.LoadFromPathAsync(configPath);
            await Assert.That(reloaded).IsNotNull();
            await Assert.That(reloaded!.DshChannel).IsEqualTo("alpha");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task LoadFromPathAsync_EmptyFile_ReturnsNullInsteadOfThrowing()
    {
        // 本回归的核心：0 字节配置是 File.Create 的正常中间态，读取端必须容错而非抛异常。
        string root = NewTempDir();
        try
        {
            string configPath = Path.Combine(root, "config", "dsh-desktop.config.json");
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            await File.WriteAllTextAsync(configPath, string.Empty);

            DshDesktopConfig? loaded = await DshDesktopConfigStore.LoadFromPathAsync(configPath);

            await Assert.That(loaded).IsNull();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task LoadFromPathAsync_TruncatedJson_ReturnsNullInsteadOfThrowing()
    {
        // 写入被打断的第二种形态：写了一半的 JSON（语法不完整）。
        string root = NewTempDir();
        try
        {
            string configPath = Path.Combine(root, "config", "dsh-desktop.config.json");
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            await File.WriteAllTextAsync(configPath, "{ \"dshChannel\": \"alp");

            DshDesktopConfig? loaded = await DshDesktopConfigStore.LoadFromPathAsync(configPath);

            await Assert.That(loaded).IsNull();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task LoadFromPathAsync_WhitespaceOnly_ReturnsNullInsteadOfThrowing()
    {
        string root = NewTempDir();
        try
        {
            string configPath = Path.Combine(root, "config", "dsh-desktop.config.json");
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            await File.WriteAllTextAsync(configPath, "   \r\n\t  ");

            DshDesktopConfig? loaded = await DshDesktopConfigStore.LoadFromPathAsync(configPath);

            await Assert.That(loaded).IsNull();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task LoadFromPathAsync_MissingFile_ReturnsNull()
    {
        string root = NewTempDir();
        Directory.CreateDirectory(root);
        try
        {
            DshDesktopConfig? loaded = await DshDesktopConfigStore.LoadFromPathAsync(
                Path.Combine(root, "config", "dsh-desktop.config.json"));

            await Assert.That(loaded).IsNull();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task LoadFromPathAsync_FileLockedByAnotherProcess_ReturnsNullInsteadOfThrowing()
    {
        // 第三种损坏形态：并发实例正在原子替换的窗口期（或杀软 / 备份工具持有句柄）时
        // File.OpenRead 抛 IOException（共享冲突）。它不属于 FileNotFoundException 子类，
        // 若不被容错捕获就会穿透到 InitializeRuntimeAsync，原症状换个姿势重现。
        string root = NewTempDir();
        try
        {
            string configPath = Path.Combine(root, "config", "dsh-desktop.config.json");
            await DshDesktopConfigStore.SaveToPathAsync(new DshDesktopConfig { DshChannel = "alpha" }, configPath);

            await using (FileStream _ = new(configPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                DshDesktopConfig? loaded = await DshDesktopConfigStore.LoadFromPathAsync(configPath);

                await Assert.That(loaded).IsNull();
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task LoadFromPathAsync_ValidJson_RoundTripsAllFields()
    {
        // 正常路径不能被容错逻辑破坏：完整往返。
        string root = NewTempDir();
        try
        {
            string configPath = Path.Combine(root, "config", "dsh-desktop.config.json");
            DshDesktopConfig original = new()
            {
                NodePath = @"C:\node\node.exe",
                DshEntryPath = @"C:\dsh\bin.js",
                DshHome = @"C:\data\dsh-home",
                Host = "127.0.0.1",
                Port = 0,
                DshChannel = "alpha",
                SafeMode = true,
                KeepRuntimeOnClose = true,
                LastRuntimePid = 28244,
                LastRuntimePort = 59228,
            };

            await DshDesktopConfigStore.SaveToPathAsync(original, configPath);
            DshDesktopConfig? loaded = await DshDesktopConfigStore.LoadFromPathAsync(configPath);

            await Assert.That(loaded).IsNotNull();
            await Assert.That(loaded!.NodePath).IsEqualTo(original.NodePath);
            await Assert.That(loaded.DshEntryPath).IsEqualTo(original.DshEntryPath);
            await Assert.That(loaded.DshHome).IsEqualTo(original.DshHome);
            await Assert.That(loaded.DshChannel).IsEqualTo("alpha");
            await Assert.That(loaded.SafeMode).IsTrue();
            await Assert.That(loaded.KeepRuntimeOnClose).IsTrue();
            await Assert.That(loaded.LastRuntimePid).IsEqualTo(28244);
            await Assert.That(loaded.LastRuntimePort).IsEqualTo(59228);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task SaveToPathAsync_DirectoryMissing_CreatesFileAndKeepsWriteAtomic()
    {
        // 首次安装：目录树不存在时仍须成功，且不残留临时文件。
        string root = NewTempDir();
        try
        {
            string configPath = Path.Combine(root, "data", "config", "dsh-desktop.config.json");

            await DshDesktopConfigStore.SaveToPathAsync(new DshDesktopConfig { DshChannel = "alpha" }, configPath);

            await Assert.That(File.Exists(configPath)).IsTrue();
            await Assert.That(Directory.GetFiles(Path.GetDirectoryName(configPath)!)).Count().IsEqualTo(1);
            await Assert.That(JsonDocument.Parse(await File.ReadAllTextAsync(configPath))).IsNotNull();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task LoadFromPathAsync_TypeMismatchedJson_ReturnsNullInsteadOfThrowing()
    {
        // 合法 JSON 但类型不匹配（外部编辑 / 别的工具写坏）：应重建而非崩溃。
        string root = NewTempDir();
        try
        {
            string configPath = Path.Combine(root, "config", "dsh-desktop.config.json");
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            await File.WriteAllTextAsync(configPath, "{ \"port\": \"not-a-number\" }");

            DshDesktopConfig? loaded = await DshDesktopConfigStore.LoadFromPathAsync(configPath);

            await Assert.That(loaded).IsNull();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task SaveToPathAsync_ConcurrentWritesToSameTarget_DoNotCollide()
    {
        // 回归核心：临时文件名若固定，两个实例共写同一 config 会撞车（File.Create 抛 IOException），
        // 冒出到启动链又变成「窗口能开、Runtime 起不来」。唯一临时名消除该竞争。
        string root = NewTempDir();
        try
        {
            string configPath = Path.Combine(root, "config", "dsh-desktop.config.json");

            Task[] writes = [.. Enumerable.Range(0, 8).Select(i =>
                DshDesktopConfigStore.SaveToPathAsync(
                    new DshDesktopConfig { DshChannel = $"ch{i}" }, configPath))];

            await Task.WhenAll(writes);

            // 全部完成后目标必须是可解析的完整 JSON，且不残留任何临时文件。
            DshDesktopConfig? loaded = await DshDesktopConfigStore.LoadFromPathAsync(configPath);
            await Assert.That(loaded).IsNotNull();
            await Assert.That(Directory.GetFiles(Path.GetDirectoryName(configPath)!)).Count().IsEqualTo(1);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string NewTempDir()
        => Path.Combine(Path.GetTempPath(), "dsh-test-" + Guid.NewGuid().ToString("N"));
}
