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

    /// <summary>是否启用 Windows 通知（默认开；关闭时崩溃 / 插件回滚事件不发气泡）。</summary>
    public bool NotificationsEnabled { get; set; } = true;

    /// <summary>DSH 更新通道（npm dist-tag：latest / alpha）。</summary>
    public string DshChannel { get; set; } = "latest";

    /// <summary>Desktop 更新通道（ADR-0003/Q7-A：首期单通道预留字段，UI 不暴露，机制待启用）。</summary>
    public string DesktopChannel { get; set; } = "stable";

    /// <summary>当前激活的自建 DSH Runtime 版本目录名；null = 借用外部安装（Electron 版）。</summary>
    public string? ActiveDshRuntime { get; set; }

    /// <summary>上一次成功启动的耗时（毫秒，Phase 8 Issue 03：Dashboard"比上次快/慢"对比基准）；首次启动为 null。</summary>
    public long? LastStartupElapsedMs { get; set; }

    /// <summary>关闭窗口后保持 DSH Runtime（ADR-0005，默认关：Session URL token 一次性，重接管当前恒退化为重启，见 ADR-0005 落地对账）。</summary>
    public bool KeepRuntimeOnClose { get; set; }

    /// <summary>异常启动自动进入安全模式（ADR-0004 修订注，默认开：连续 2 次启动失败 → 自动 SafeMode + 通知）。</summary>
    public bool AutoSafeModeOnFailure { get; set; } = true;

    /// <summary>启动时检查网络更新（§34 修订注，默认关：用户显式开启才破例）。</summary>
    public bool CheckUpdatesOnStartup { get; set; }

    /// <summary>上次 Running 的 Runtime 进程 ID（ADR-0005 重接管探测依据；非 Session 数据，允许落盘）。</summary>
    public int? LastRuntimePid { get; set; }

    /// <summary>上次 Running 的 Runtime 监听端口（同上）。</summary>
    public int? LastRuntimePort { get; set; }

    /// <summary>关闭窗口最小化到托盘（Phase 8 Issue 05，默认开：关窗拦截为隐藏，宿主与 Runtime 保持运行，托盘菜单可退出）。</summary>
    public bool MinimizeToTrayOnClose { get; set; } = true;

    /// <summary>开机自动启动（Phase 8 Issue 05，默认关：HKCU Run 键写入当前 exe 路径，未安装形态同样持久化）。</summary>
    public bool LaunchOnStartup { get; set; }

    /// <summary>后台检查更新（Phase 8 Issue 05，默认开：UI Ready 后异步静默检查；与"启动时检查"开关独立）。</summary>
    public bool BackgroundUpdateCheck { get; set; } = true;

    /// <summary>自动下载安装（Phase 8 Issue 05，默认关：开 = 检查发现 Desktop 更新后后台预下载，应用重启仍需用户确认）。</summary>
    public bool AutoDownloadUpdates { get; set; }
}

/// <summary>
/// 表示配置的 JSON 序列化上下文（AOT 源生成，禁止反射序列化）。
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DshDesktopConfig))]
public sealed partial class DshConfigJsonContext : JsonSerializerContext;

/// <summary>
/// 表示配置的加载、自动探测与回写（Q2 决策：配置文件持久化 + 缺失时探测并回写）。
/// </summary>
public static class DshDesktopConfigStore
{
    /// <summary>
    /// 覆盖数据根的环境变量名（测试 / 便携场景可显式指定，优先级最高）。
    /// </summary>
    internal const string DataRootEnvironmentVariable = "DSH_DESKTOP_DATA_ROOT";

    /// <summary>
    /// 获取数据根目录（ADR-0003 修订：默认跟随安装盘，避免用户数据落系统盘 C 盘）。
    /// 解析顺序：环境变量 → 安装根同级 data → %LOCALAPPDATA% 兜底。
    /// Velopack 更新只替换安装根下的 current\ 目录，data\ 与 current\ 平级不受影响。
    /// </summary>
    public static string DataRoot { get; } = ResolveDataRoot(
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DshDesktop", "data"),
        ResolveInstallRoot(AppContext.BaseDirectory),
        Environment.GetEnvironmentVariable(DataRootEnvironmentVariable));

    /// <summary>
    /// 解析数据根：环境变量优先，其次安装根同级 data，最后回退默认根。
    /// internal 供 DshDesktop.Tests 直测（InternalsVisibleTo）；入参注入使用例确定且无副作用。
    /// </summary>
    /// <param name="defaultRoot">兜底数据根（%LOCALAPPDATA%\DshDesktop\data）。</param>
    /// <param name="installRoot">安装根；null 表示未安装形态（便携 / dotnet run）。</param>
    /// <param name="environmentOverride">环境变量覆盖值。</param>
    /// <returns>最终数据根绝对路径。</returns>
    internal static string ResolveDataRoot(
        string defaultRoot,
        string? installRoot,
        string? environmentOverride)
    {
        if (!string.IsNullOrWhiteSpace(environmentOverride))
        {
            return environmentOverride;
        }

        return string.IsNullOrWhiteSpace(installRoot)
            ? defaultRoot
            : Path.Combine(installRoot, "data");
    }

    /// <summary>
    /// 解析安装根：Velopack 已安装形态下 exe 位于 &lt;安装根&gt;\current\，上跳一级即安装根；
    /// 未安装形态（开发 bin 目录 / 便携解压）返回 null，由调用方回退默认根。
    /// internal 供 DshDesktop.Tests 直测（InternalsVisibleTo）。
    /// </summary>
    /// <param name="baseDirectory">exe 所在目录（生产传 AppContext.BaseDirectory）。</param>
    /// <returns>安装根；非安装形态为 null。</returns>
    internal static string? ResolveInstallRoot(string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            return null;
        }

        try
        {
            // BaseDirectory 形如 "<安装根>\current\"：去尾分隔符后取末段判断是否 current。
            string trimmed = baseDirectory.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (trimmed.Length == 0)
            {
                return null;
            }

            // 仅当 exe 位于名为 current 的目录下才认定是 Velopack 安装根，
            // 避免开发形态（bin\Debug\net10.0-windows）被误判。
            return string.Equals(Path.GetFileName(trimmed), "current", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(trimmed)
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 获取配置文件路径（ADR-0003：数据根 config 子目录——exe 旁会随 Velopack 更新被替换，§39）。
    /// </summary>
    public static string ConfigPath { get; } =
        Path.Combine(DataRoot, "config", "dsh-desktop.config.json");

    /// <summary>
    /// 重锚配置中的 <c>dshHome</c> 到当前数据根（ADR-0003 修订：数据根从系统盘迁到安装盘后，
    /// 已落盘配置仍指向旧位置，必须重锚，否则数据继续写旧盘）。
    /// internal 供 DshDesktop.Tests 直测（InternalsVisibleTo）。
    /// </summary>
    /// <param name="config">配置实例（原地修改）。</param>
    /// <param name="newDataRoot">新数据根。</param>
    /// <returns>是否发生了修改。</returns>
    internal static bool RebaseDshHome(DshDesktopConfig config, string newDataRoot)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(newDataRoot);

        string expected = Path.Combine(newDataRoot, "dsh-home");
        if (string.Equals(config.DshHome, expected, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        config.DshHome = expected;
        return true;
    }

    /// <summary>
    /// 旧版配置路径（exe 旁），仅用于一次性迁移。
    /// </summary>
    private static string LegacyConfigPath { get; } =
        Path.Combine(AppContext.BaseDirectory, "dsh-desktop.config.json");

    /// <summary>
    /// Desktop 自带的 harness-node-entry.mjs 垫片路径（exe 旁 resources\ 下，随包分发）。
    /// 背景：dsh ≥0.1.5-rc.1 的 bin.js 以 import.meta.main 守门，旧 Electron harness 的
    /// 纯 import() 加载方式下入口不会自执行（进程静默退出 0）；自带垫片在 import 后
    /// 显式调用 runCli 导出以兼容新旧入口。
    /// </summary>
    private static string OwnHarnessEntryPath { get; } =
        Path.Combine(AppContext.BaseDirectory, "resources", "harness-node-entry.mjs");

    /// <summary>
    /// 解析 harness 垫片路径：自带垫片存在则优先（隔离旧 Electron 依赖），否则回退借用版。
    /// internal 供 DshDesktop.Tests 直测（InternalsVisibleTo）。
    /// </summary>
    /// <param name="ownHarnessPath">自带垫片候选路径。</param>
    /// <param name="electronHarnessPath">借用的 Electron 垫片候选路径。</param>
    /// <returns>可用的垫片路径；两者皆不存在时为 null。</returns>
    internal static string? ResolveHarnessEntryPath(string? ownHarnessPath, string? electronHarnessPath)
    {
        if (!string.IsNullOrWhiteSpace(ownHarnessPath) && File.Exists(ownHarnessPath))
        {
            return ownHarnessPath;
        }

        return !string.IsNullOrWhiteSpace(electronHarnessPath) && File.Exists(electronHarnessPath)
            ? electronHarnessPath
            : null;
    }

    /// <summary>
    /// 重锚配置中的 harness 垫片路径：配置缺失或指向的文件已不存在，且自带垫片可用时切换到自带垫片。
    /// 配置仍指向有效文件时不动（尊重用户 / 手工修复的选择）。
    /// internal 供 DshDesktop.Tests 直测（InternalsVisibleTo）。
    /// </summary>
    /// <param name="config">配置实例（原地修改）。</param>
    /// <param name="ownHarnessPath">自带垫片候选路径。</param>
    /// <returns>是否发生了修改。</returns>
    internal static bool ReanchorHarnessEntryPath(DshDesktopConfig config, string? ownHarnessPath)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (string.IsNullOrWhiteSpace(ownHarnessPath) || !File.Exists(ownHarnessPath))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(config.HarnessNodeEntryPath)
            && File.Exists(config.HarnessNodeEntryPath))
        {
            return false;
        }

        config.HarnessNodeEntryPath = ownHarnessPath;
        return true;
    }

    /// <summary>
    /// 加载配置；文件不存在时自动探测并回写。
    /// </summary>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>配置实例。</returns>
    public static async Task<DshDesktopConfig> LoadOrDetectAsync(CancellationToken cancellationToken = default)
    {
        MigrateLegacyConfigIfNeeded(LegacyConfigPath, ConfigPath);

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

                // 数据根迁移：配置中的 dshHome 与当前数据根不一致时重锚（如 C 盘 → 安装盘）。
                if (RebaseDshHome(loaded, DataRoot))
                {
                    dirty = true;
                }

                // harness 垫片迁移：配置缺失 / 指向失效且自带垫片可用时重锚到自带垫片。
                if (ReanchorHarnessEntryPath(loaded, OwnHarnessEntryPath))
                {
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
    /// 回写配置到数据根 config 目录。
    /// </summary>
    /// <param name="config">配置实例。</param>
    /// <param name="cancellationToken">取消标记。</param>
    public static Task SaveAsync(DshDesktopConfig config, CancellationToken cancellationToken = default)
        => SaveToPathAsync(config, ConfigPath, cancellationToken);

    /// <summary>
    /// 确保配置文件所在目录存在（首次全新安装时数据根内尚无 config 目录，
    /// 而 File.Create 不创建父目录——缺此步骤会以 DirectoryNotFoundException 中断启动）。
    /// internal 供 DshDesktop.Tests 直测（InternalsVisibleTo）。
    /// </summary>
    /// <param name="configPath">配置文件完整路径。</param>
    internal static void EnsureConfigDirectory(string configPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        if (Path.GetDirectoryName(configPath) is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
        }
    }

    /// <summary>
    /// 回写配置到指定路径（路径可注入，供测试覆盖首次安装场景）。
    /// internal 供 DshDesktop.Tests 直测（InternalsVisibleTo）。
    /// </summary>
    /// <param name="config">配置实例。</param>
    /// <param name="configPath">目标配置文件路径。</param>
    /// <param name="cancellationToken">取消标记。</param>
    internal static async Task SaveToPathAsync(
        DshDesktopConfig config,
        string configPath,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigDirectory(configPath);
        await using FileStream writeStream = File.Create(configPath);
        await JsonSerializer
            .SerializeAsync(writeStream, config, DshConfigJsonContext.Default.DshDesktopConfig, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// ADR-0003：exe 旁旧配置一次性迁移到数据根（File.Move = 读+写新+删旧的原子等价物，同卷元数据操作）。
    /// internal 供 DshDesktop.Tests 直测（InternalsVisibleTo）。
    /// </summary>
    internal static void MigrateLegacyConfigIfNeeded(string legacyPath, string configPath)
    {
        if (!File.Exists(configPath) && File.Exists(legacyPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            File.Move(legacyPath, configPath);
        }
    }

    private static DshDesktopConfig Detect()
    {
        string dshHome = Path.Combine(DataRoot, "dsh-home");

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
                // 自带 harness 垫片与 Electron 无关，缺失时不设（Detect 结果原样回写）。
                HarnessNodeEntryPath = ResolveHarnessEntryPath(OwnHarnessEntryPath, null),
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
            HarnessNodeEntryPath = ResolveHarnessEntryPath(OwnHarnessEntryPath, harnessEntry),
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
