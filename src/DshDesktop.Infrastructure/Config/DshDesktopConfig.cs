using System.Text.Json;
using System.Text.Json.Serialization;

namespace DshDesktop.Infrastructure.Config;

/// <summary>
/// 表示 DSH Desktop 的本地配置（exe 旁 dsh-desktop.config.json）。
/// </summary>
public sealed class DshDesktopConfig
{
    /// <summary>node.exe 路径。</summary>
    public string NodePath { get; set; } = string.Empty;

    /// <summary>DSH CLI 入口（@deepseek-ai/dsh/lib/bin.js）路径。</summary>
    public string DshEntryPath { get; set; } = string.Empty;

    /// <summary>harness-node-entry.mjs 垫片路径；为 null 时直接以 DshEntryPath 启动。</summary>
    public string? HarnessNodeEntryPath { get; set; }

    /// <summary>DSH 进程工作目录。</summary>
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>DSH_HOME 数据根目录。</summary>
    public string DshHome { get; set; } = string.Empty;

    /// <summary>监听地址。</summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>监听端口；0 表示启动时探测空闲端口（ADR-0001）。</summary>
    public int Port { get; set; }

    /// <summary>启动 + 就绪等待超时（秒）。</summary>
    public int StartupTimeoutSeconds { get; set; } = 120;

    /// <summary>Profile 种子来源 harness 数据目录；null 或已存在种子目标时跳过。</summary>
    public string? SeedProfileFrom { get; set; }

    /// <summary>vendored pnpm 入口（pnpm.cjs）路径；null 时 lockfile 重建不可用。</summary>
    public string? PnpmCjsPath { get; set; }

    /// <summary>npm CLI 入口（npm-cli.js）路径；null 时 Runtime 自举安装不可用。</summary>
    public string? NpmCjsPath { get; set; }

    /// <summary>是否处于安全模式（跨重启持久）：抑制 Runtime 自动启动，仅保留管理界面。</summary>
    public bool SafeMode { get; set; }

    /// <summary>DSH 更新通道（npm dist-tag：latest / alpha）。</summary>
    public string DshChannel { get; set; } = "latest";

    /// <summary>当前激活的自建 DSH Runtime 版本目录名；null = 借用外部安装（Electron 版）。</summary>
    public string? ActiveDshRuntime { get; set; }
}

/// <summary>
/// 表示配置的 JSON 序列化上下文（AOT 源生成，禁止反射序列化）。
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DshDesktopConfig))]
public sealed partial class DshConfigJsonContext : JsonSerializerContext;

/// <summary>
/// 表示配置的加载、自动探测与回写（Q2 决策：exe 旁文件，缺失时探测并回写）。
/// </summary>
public static class DshDesktopConfigStore
{
    /// <summary>
    /// 获取配置文件路径（exe 旁）。
    /// </summary>
    public static string ConfigPath { get; } =
        Path.Combine(AppContext.BaseDirectory, "dsh-desktop.config.json");

    /// <summary>
    /// 加载配置；文件不存在时自动探测并回写。
    /// </summary>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>配置实例。</returns>
    public static async Task<DshDesktopConfig> LoadOrDetectAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(ConfigPath))
        {
            DshDesktopConfig? loaded;
            await using (FileStream readStream = File.OpenRead(ConfigPath))
            {
                loaded = await JsonSerializer
                    .DeserializeAsync(readStream, DshConfigJsonContext.Default.DshDesktopConfig, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (loaded is not null)
            {
                // 配置迁移：老配置缺少工具路径时推导补全并回写。
                bool dirty = false;
                if (string.IsNullOrEmpty(loaded.PnpmCjsPath)
                    && DerivePnpmCjsPath(loaded.DshEntryPath) is { } derivedPnpm)
                {
                    loaded.PnpmCjsPath = derivedPnpm;
                    dirty = true;
                }

                if (string.IsNullOrEmpty(loaded.NpmCjsPath)
                    && DeriveNpmCjsPath(loaded.NodePath) is { } derivedNpm)
                {
                    loaded.NpmCjsPath = derivedNpm;
                    dirty = true;
                }

                if (dirty)
                {
                    await SaveAsync(loaded, cancellationToken).ConfigureAwait(false);
                }

                return loaded;
            }
        }

        DshDesktopConfig detected = Detect();
        await SaveAsync(detected, cancellationToken).ConfigureAwait(false);
        return detected;
    }

    /// <summary>
    /// 回写配置到 exe 旁。
    /// </summary>
    /// <param name="config">配置实例。</param>
    /// <param name="cancellationToken">取消标记。</param>
    public static async Task SaveAsync(DshDesktopConfig config, CancellationToken cancellationToken = default)
    {
        await using FileStream writeStream = File.Create(ConfigPath);
        await JsonSerializer
            .SerializeAsync(writeStream, config, DshConfigJsonContext.Default.DshDesktopConfig, cancellationToken)
            .ConfigureAwait(false);
    }

    private static DshDesktopConfig Detect()
    {
        string dshHome = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DshDesktop", "data", "dsh-home");

        string? resourcesDir = FindElectronResourcesDir();

        string? seedFrom = null;
        string electronHarness = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "dsh-desktop", "harness");
        if (Directory.Exists(Path.Combine(electronHarness, "profiles", "web")))
        {
            seedFrom = electronHarness;
        }

        if (resourcesDir is null)
        {
            // 未探测到 Electron 安装：留下空路径，启动时以明确错误反馈（诚实失败）。
            return new DshDesktopConfig
            {
                DshHome = dshHome,
                SeedProfileFrom = seedFrom,
            };
        }

        string appDir = Path.Combine(resourcesDir, "app");
        string vendoredNode = Path.Combine(appDir, "node_modules", "node", "bin", "node.exe");
        string harnessEntry = Path.Combine(resourcesDir, "harness-node-entry.mjs");
        string dshEntry = Path.Combine(appDir, "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js");

        return new DshDesktopConfig
        {
            NodePath = File.Exists(vendoredNode) ? vendoredNode : "node",
            DshEntryPath = dshEntry,
            HarnessNodeEntryPath = File.Exists(harnessEntry) ? harnessEntry : null,
            WorkingDirectory = appDir,
            DshHome = dshHome,
            SeedProfileFrom = seedFrom,
            PnpmCjsPath = DerivePnpmCjsPath(dshEntry),
            NpmCjsPath = DeriveNpmCjsPath(File.Exists(vendoredNode) ? vendoredNode : "node"),
        };
    }

    /// <summary>
    /// 推导 npm-cli.js：node 二进制旁的 node_modules/npm（系统 node 自带；
    /// vendored node 纯二进制包不含 npm，回退到 PATH 上的系统 node）。
    /// internal 供 DshDesktop.Tests 直测（InternalsVisibleTo）。
    /// </summary>
    internal static string? DeriveNpmCjsPath(string? nodePath)
    {
        if (!string.IsNullOrWhiteSpace(nodePath)
            && File.Exists(nodePath)
            && Path.GetDirectoryName(nodePath) is { } nodeDir)
        {
            string candidate = Path.Combine(nodeDir, "node_modules", "npm", "bin", "npm-cli.js");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        // PATH 上的系统 node。
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (pathEnv is not null)
        {
            foreach (string dir in pathEnv.Split(Path.PathSeparator))
            {
                if (dir.Length == 0)
                {
                    continue;
                }

                string candidate = Path.Combine(dir, "node_modules", "npm", "bin", "npm-cli.js");
                if (File.Exists(candidate) && File.Exists(Path.Combine(dir, "node.exe")))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 从 DSH 入口路径推导 vendored pnpm.cjs（@deepseek-ai/dsh/lib/bin.js → node_modules/pnpm/bin/pnpm.cjs）。
    /// internal 供 DshDesktop.Tests 直测（InternalsVisibleTo）。
    /// </summary>
    internal static string? DerivePnpmCjsPath(string? dshEntryPath)
    {
        if (string.IsNullOrWhiteSpace(dshEntryPath))
        {
            return null;
        }

        // bin.js → lib → @deepseek-ai/dsh → @deepseek-ai → node_modules。
        DirectoryInfo? nodeModulesDir = Path.GetDirectoryName(dshEntryPath) is { } lib
            ? new DirectoryInfo(lib).Parent?.Parent?.Parent
            : null;
        if (nodeModulesDir is null)
        {
            return null;
        }

        string pnpmCjs = Path.Combine(nodeModulesDir.FullName, "pnpm", "bin", "pnpm.cjs");
        return File.Exists(pnpmCjs) ? pnpmCjs : null;
    }

    private static string? FindElectronResourcesDir()
    {List<string> candidates =
        [
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "DSH Desktop", "resources"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "DSH Desktop", "resources"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "DSH Desktop", "resources"),
        ];

        // 安装目录可由用户自由选择（§37 示例即 D:\DSH Desktop）：
        // 扫描所有固定盘的 Program Files，覆盖非系统盘安装。
        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType is DriveType.Fixed)
            {
                candidates.Add(Path.Combine(drive.RootDirectory.FullName, "Program Files", "DSH Desktop", "resources"));
            }
        }

        return candidates.FirstOrDefault(dir =>
            File.Exists(Path.Combine(dir, "app", "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js")));
    }
}
