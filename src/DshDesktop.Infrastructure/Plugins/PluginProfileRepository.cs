using System.Diagnostics;
using System.Text.Json.Nodes;
using DshDesktop.Application.Plugins;
using DshDesktop.Domain.Plugins;
using YamlDotNet.RepresentationModel;

namespace DshDesktop.Infrastructure.Plugins;

/// <summary>
/// 表示 Profile 文件级插件仓库（§18：DSH 不启动也可管理插件；
/// 卸载复刻 Electron detachLegacyPlugin 四步，docs/DSH-Plugin-Mechanics.md §5）。
/// </summary>
public sealed class PluginProfileRepository(
    string profileDir,
    string nodePath,
    string? pnpmCjsPath,
    Func<string, string, string, string[], CancellationToken, Task<(int ExitCode, string OutputTail)>>? runOnce = null,
    Action<string>? onWarning = null,
    Func<CancellationToken, Task<bool>>? gitAvailable = null)
    : IPluginManager
{
    private const string CoreScope = "@deepseek-ai/";
    private const string CoreMarketName = "dshmarket";

    private readonly Func<string, string, string, string[], CancellationToken, Task<(int ExitCode, string OutputTail)>>? _runOnce = runOnce;
    private readonly Action<string>? _onWarning = onWarning;

    // git 可用性探针（git 协议源前置检查用；测试注入桩，生产探测 PATH 上的 git）。
    private readonly Func<CancellationToken, Task<bool>> _gitAvailable = gitAvailable ?? ProbeGitOnPathAsync;

    private string ManifestPath => Path.Combine(profileDir, "package.json");
    private string PatchPath => Path.Combine(profileDir, "cordis.patch.yml");

    /// <inheritdoc />
    public Task<IReadOnlyList<PluginInfo>> ListPluginsAsync(CancellationToken cancellationToken)
    {
        JsonObject manifest = ReadManifest();
        HashSet<string> bundles = ReadBundles(manifest);
        HealCoreBundles(manifest, bundles);
        List<PluginInfo> plugins = [];

        if (manifest["dependencies"] is JsonObject dependencies)
        {
            foreach ((string name, JsonNode? _) in dependencies)
            {
                (string version, string description) = ReadInstalledInfo(name);
                bool resolvable = ProfileBundleProbe.IsBundleResolved(profileDir, name);
                plugins.Add(new PluginInfo(
                    name,
                    version,
                    IsCore(name),
                    bundles.Contains(name),
                    description,
                    resolvable));
            }
        }

        return Task.FromResult<IReadOnlyList<PluginInfo>>(plugins);
    }

    /// <inheritdoc />
    public Task HealCoreBundlesAsync(CancellationToken cancellationToken)
    {
        JsonObject manifest = ReadManifest();
        HealCoreBundles(manifest, ReadBundles(manifest));
        return Task.CompletedTask;
    }

    /// <summary>
    /// 把缺失于 bundles 的核心插件写回（磁盘可解析才写，见接口文档）；有改动才落盘一次。
    /// </summary>
    private void HealCoreBundles(JsonObject manifest, HashSet<string> bundles)
    {
        if (manifest["dependencies"] is not JsonObject dependencies)
        {
            return;
        }

        bool healed = false;
        foreach ((string name, JsonNode? _) in dependencies)
        {
            if (!IsCore(name)
                || bundles.Contains(name)
                || !ProfileBundleProbe.IsBundleResolved(profileDir, name))
            {
                continue;
            }

            // JsonArray.Add<T> 泛型重载带 IL2026/IL3050 标注（AOT 不友好），
            // 经 ICollection<JsonNode?> 接口走非泛型实现（同 SetEnabledAsync）。
            ((System.Collections.Generic.ICollection<JsonNode?>)GetOrCreateBundlesArray(manifest))
                .Add(JsonValue.Create(name));
            bundles.Add(name);
            healed = true;
        }

        if (healed)
        {
            WriteManifest(manifest);
        }
    }

    /// <inheritdoc />
    public Task SetEnabledAsync(string name, bool enabled, CancellationToken cancellationToken)
    {
        GuardThirdParty(name);

        JsonObject manifest = ReadManifest();
        bool installed = (manifest["dependencies"] as JsonObject)?.ContainsKey(name) == true;
        if (enabled && !installed)
        {
            throw new InvalidOperationException($"插件 {name} 未安装，无法启用。");
        }

        JsonArray bundles = GetOrCreateBundlesArray(manifest);
        JsonNode? existing = bundles.FirstOrDefault(
            node => node is JsonValue value && value.TryGetValue(out string? text) && text == name);

        if (enabled && existing is null)
        {
            // JsonArray.Add<T> 泛型重载带 IL2026/IL3050 标注（AOT 不友好），
            // 经 ICollection<JsonNode?> 接口走非泛型实现。
            ((System.Collections.Generic.ICollection<JsonNode?>)bundles).Add(JsonValue.Create(name));
            WriteManifest(manifest);
        }
        else if (!enabled && existing is not null)
        {
            bundles.Remove(existing);
            WriteManifest(manifest);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task UninstallAsync(string name, CancellationToken cancellationToken)
    {
        GuardThirdParty(name);

        // ① package.json：删 dependency + bundles 项。
        JsonObject manifest = ReadManifest();
        (manifest["dependencies"] as JsonObject)?.Remove(name);
        JsonArray bundles = GetOrCreateBundlesArray(manifest);
        JsonNode? bundleEntry = bundles.FirstOrDefault(
            node => node is JsonValue value && value.TryGetValue(out string? text) && text == name);
        if (bundleEntry is not null)
        {
            bundles.Remove(bundleEntry);
        }

        WriteManifest(manifest);

        // ② cordis.patch.yml：清该插件条目（仅在有删除时重写，保住无操作场景的注释）。
        CleanPatchEntries(name);

        // ③ node_modules/<pkg>：删实目录（hoisted 布局）。
        string packageDir = Path.Combine(profileDir, "node_modules", name);
        if (Directory.Exists(packageDir))
        {
            Directory.Delete(packageDir, recursive: true);
        }

        // ④ 重建 lockfile（vendored pnpm，优先 --offline 保 §34）。
        await RebuildLockfileAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string> InstallAsync(string source, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (string.IsNullOrWhiteSpace(pnpmCjsPath) || !File.Exists(pnpmCjsPath))
        {
            throw new InvalidOperationException(
                "找不到 vendored pnpm（pnpmCjsPath），无法安装插件。请检查 dsh-desktop.config.json。");
        }

        HashSet<string> dependenciesBefore = ReadDependencyNames();
        // 安装前快照不可解析集合：事后只对「本次安装新增」的不可解析条目抛错回滚，
        // 既有损坏降级 WARN（不为历史问题回滚无辜的本次安装）。
        string[] unresolvedBefore = ProfileBundleProbe.UnresolvedBundles(profileDir);

        // 本地文件（.tgz / 目录）走 --offline；registry 包名与远端源（GitHub 等）允许联网。
        bool isLocalSource = File.Exists(source) || Directory.Exists(source);

        // git 协议源前置检查：PATH 上没有 git 时 pnpm 拉取必然失败，提前给出可操作报错
        // （tarball URL 直接下载解压，不在此列）。
        if (!isLocalSource && GitInstallSource.RequiresGit(source)
            && !await _gitAvailable(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"检测到 Git 仓库源（{source}），但系统 PATH 上找不到 git，pnpm 无法拉取。" +
                "请安装 git 后重试，或改用 tarball 地址（无需 git）：" +
                "https://github.com/<owner>/<repo>/archive/refs/heads/<branch>.tar.gz");
        }

        string[] arguments = isLocalSource
            ? ["add", source, "--offline"]
            : ["add", source];

        // pnpm add 前归一化清单（剥离代际投影残留 + 悬空 overrides）：
        // 否则 pnpm add 会把已 materialize 的真实依赖重建为悬空 junction，打坏 profile。
        ProfileManifestFixups.NormalizeToFlatModel(profileDir);

        (int exitCode, string outputTail) = await NodeJsToolRunner.RunAsync(
            nodePath, pnpmCjsPath!, profileDir, arguments, cancellationToken, _runOnce).ConfigureAwait(false);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"pnpm add 失败（退出码 {exitCode}）。{outputTail}");
        }

        // 解析实际安装的包名：优先 dependencies 差集，重装场景回退到 source 字符串解析。
        HashSet<string> dependenciesAfter = ReadDependencyNames();
        dependenciesAfter.ExceptWith(dependenciesBefore);
        string pluginName = dependenciesAfter.Count == 1
            ? dependenciesAfter.First()
            : ResolveNameFromSource(source);

        ValidateAndReconcileInstalledBundle(pluginName);
        ThrowIfUnbuiltGitSource(source, pluginName);
        await EnsureDeclaredBundlesResolvableAsync(unresolvedBefore, cancellationToken).ConfigureAwait(false);
        return pluginName;
    }

    /// <summary>
    /// git 源码仓未提供构建的判定与报错：patch 声明指向的 cordis.patch.yml 不存在、
    /// main 入口缺失、manifest 也无 prepare/prepack 脚本 —— pnpm 不会代为构建，
    /// 责任在插件作者；宿主不猜工具链，只给出可操作指引并交给事务回滚。
    /// </summary>
    private void ThrowIfUnbuiltGitSource(string source, string pluginName)
    {
        // 本地/registry 源由发布方保证产物完整，不参与此判定。
        if (File.Exists(source) || Directory.Exists(source) || !GitInstallSource.IsRemoteSource(source))
        {
            return;
        }

        string packageDir = Path.Combine(profileDir, "node_modules", pluginName);
        string manifestPath = Path.Combine(packageDir, "package.json");
        if (!File.Exists(manifestPath))
        {
            return; // 由后续 bundle 可解析性校验命名缺失。
        }

        JsonNode? manifest = JsonNode.Parse(File.ReadAllText(manifestPath));
        // patch 声明可能是字符串路径（"./cordis.patch.yml"）也可能是对象（{} 占位种子），
        // 非 JsonValue 一律按「无法定位产物文件」处理。
        string? patch = manifest?["dsh"]?["bundle"]?["patch"] is JsonValue patchValue
            && patchValue.TryGetValue(out string? patchText) ? patchText : null;
        bool patchMissing = patch is null
            || !File.Exists(Path.Combine(packageDir, patch));
        string? main = manifest?["main"] is JsonValue mainValue
            && mainValue.TryGetValue(out string? mainText) ? mainText : null;
        bool mainMissing = main is null
            || !File.Exists(Path.Combine(packageDir, main));
        bool hasBuildScript = manifest?["scripts"] is JsonObject scripts
            && (scripts.ContainsKey("prepare") || scripts.ContainsKey("prepack"));

        if (patchMissing && mainMissing && !hasBuildScript)
        {
            throw new InvalidOperationException(
                $"{pluginName} 是未构建的 GitHub 源码仓：既无构建产物（cordis.patch.yml / {main ?? "main 入口"}），" +
                "package.json 也没有 prepare/prepack 脚本，pnpm 无法自动构建。" +
                "这是插件仓库未提供发布构建（插件作者侧问题），请改用其发布的 npm 包 / tarball，" +
                "或联系作者补充 prepare 构建脚本。已回滚。");
        }
    }

    private void ValidateAndReconcileInstalledBundle(string pluginName)
    {
        // 校验是 DSH 插件（声明 dsh.bundle.patch），不是则抛错交给事务回滚。
        string installedManifestPath = Path.Combine(profileDir, "node_modules", pluginName, "package.json");
        JsonNode? installedManifest = ProfileBundleProbe.IsBundleResolved(profileDir, pluginName)
            ? JsonNode.Parse(File.ReadAllText(installedManifestPath))
            : null;
        if (installedManifest?["dsh"]?["bundle"]?["patch"] is null)
        {
            throw new InvalidOperationException(
                $"{pluginName} 不是 DSH 插件（未声明 dsh.bundle.patch），已回滚。");
        }

        // Reconcile（docs/DSH-Plugin-Mechanics.md §3）：声明 dsh.bundle 的 dependency 追加到 bundles 末尾。
        JsonObject manifest = ReadManifest();
        JsonArray bundles = GetOrCreateBundlesArray(manifest);
        bool alreadyInBundles = bundles.Any(
            node => node is JsonValue value && value.TryGetValue(out string? text) && text == pluginName);
        if (!alreadyInBundles)
        {
            ((System.Collections.Generic.ICollection<JsonNode?>)bundles).Add(JsonValue.Create(pluginName));
            WriteManifest(manifest);
        }
    }

    private async Task EnsureDeclaredBundlesResolvableAsync(string[] unresolvedBefore, CancellationToken cancellationToken)
    {
        // 安装后磁盘校验（2026-09-14 实机：pnpm add 退出码 0 不代表 bundle 可解析——
        // 悬空 junction / 中断安装残留会让 DSH 启动期 resolveBundleDir 抛
        // "cannot resolve profile bundle" → Runtime ExitCode=1，而错误只在重启时才暴露）。
        // 先把复制/安装可能遗留的悬空联接点实体化（无种子源时仅尽力），再校验全部声明 bundle。
        ReparsePointMaterializer.Materialize(
            Path.Combine(profileDir, "node_modules"), sourceNodeModulesDir: null);
        string[] unresolved = ProfileBundleProbe.UnresolvedBundles(profileDir);
        if (unresolved.Length > 0)
        {
            // 先摘除「不在 dependencies」的不可恢复声明（历史安装/卸载残留）：pnpm 只物化
            // dependencies，重建 lockfile 永不可能修复它，留着会让每次安装都被同一残留卡死
            // （2026-10-10 实机：bundles 残留实验插件声明，装 dshmarket 反复回滚）。
            HashSet<string> dependencies = ReadDependencyNames();
            string[] stale = [.. unresolved.Where(bundle => !dependencies.Contains(bundle))];
            if (stale.Length > 0)
            {
                PruneStaleBundleDeclarations(stale);
                unresolved = ProfileBundleProbe.UnresolvedBundles(profileDir);
            }
        }

        if (unresolved.Length > 0)
        {
            // 修复路径：重建 lockfile（pnpm install --no-frozen-lockfile）让依赖树重新物化。
            // 修复失败也要落到「命名不可解析 bundle」的精确报错，而非被 lockfile 错误掩盖。
            Exception? rebuildFailure = null;
            try
            {
                await RebuildLockfileAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception repairEx)
            {
                rebuildFailure = repairEx;
            }

            ReportOrThrowUnresolved(unresolvedBefore, rebuildFailure);
        }
    }

    private void ReportOrThrowUnresolved(string[] unresolvedBefore, Exception? rebuildFailure)
    {
        string[] unresolved = ProfileBundleProbe.UnresolvedBundles(profileDir);
        if (unresolved.Length == 0)
        {
            return;
        }

        // 只对「本次安装新增」的不可解析条目抛错回滚；既有损坏（安装前已存在）降级 WARN——
        // 不为历史问题回滚无辜的本次安装，Runtime 启动期仍会精确报 cannot resolve profile bundle。
        HashSet<string> before = new(unresolvedBefore, StringComparer.Ordinal);
        string[] fresh = [.. unresolved.Where(bundle => !before.Contains(bundle))];
        if (fresh.Length > 0)
        {
            throw new InvalidOperationException(
                $"插件安装后仍有不可解析的 bundle：{string.Join(", ", fresh)}。" +
                (rebuildFailure is not null ? $"依赖树重建也失败（{rebuildFailure.Message}）。" : "") +
                "请检查 Profile 依赖树或重新种子 Profile。");
        }

        _onWarning?.Invoke(
            $"既有 bundle 损坏未修复：{string.Join(", ", unresolved)}。" +
            "本次安装未受影响；Runtime 启动仍会报 cannot resolve profile bundle，请修复依赖或重新种子 Profile。");
    }

    private void PruneStaleBundleDeclarations(string[] stale)
    {
        JsonObject manifest = ReadManifest();
        JsonArray bundles = GetOrCreateBundlesArray(manifest);
        foreach (string name in stale)
        {
            JsonNode? entry = bundles.FirstOrDefault(
                node => node is JsonValue value && value.TryGetValue(out string? text) && text == name);
            if (entry is not null)
            {
                bundles.Remove(entry);
            }
        }

        WriteManifest(manifest);
        _onWarning?.Invoke(
            $"Profile 清单残留不可恢复的 bundle 声明（不在 dependencies 且磁盘缺失），已自动摘除：{string.Join(", ", stale)}。");
    }

    private HashSet<string> ReadDependencyNames()
    {
        JsonObject manifest = ReadManifest();
        HashSet<string> names = new(StringComparer.Ordinal);
        if (manifest["dependencies"] is JsonObject dependencies)
        {
            foreach ((string name, JsonNode? _) in dependencies)
            {
                names.Add(name);
            }
        }

        return names;
    }

    private string ResolveNameFromSource(string source)
    {
        // 本地路径：文件名。
        if (File.Exists(source) || Directory.Exists(source))
        {
            return Path.GetFileNameWithoutExtension(source.TrimEnd(Path.DirectorySeparatorChar));
        }

        // 远端源（git/URL/tarball）：repo 名 ≠ package 名（如 @scope/pkg），
        // 按清单 dependencies 值反查键名；未命中再以 URL 末段兜底。
        if (GitInstallSource.IsRemoteSource(source))
        {
            if (ReadManifest()["dependencies"] is JsonObject dependencies)
            {
                string wanted = GitInstallSource.NormalizeSpec(source);
                foreach ((string name, JsonNode? value) in dependencies)
                {
                    if (value is JsonValue jsonValue
                        && jsonValue.TryGetValue(out string? spec)
                        && string.Equals(GitInstallSource.NormalizeSpec(spec), wanted, StringComparison.Ordinal))
                    {
                        return name;
                    }
                }
            }

            return GitInstallSource.LeafName(source);
        }

        // registry："name@version" / "@scope/name@version"。
        int atIndex = source.LastIndexOf('@');
        return atIndex > 0 ? source[..atIndex] : source;
    }

    /// <summary>探测系统 PATH 上的 git（git --version）；找不到/启动失败一律视为不可用。</summary>
    private static async Task<bool> ProbeGitOnPathAsync(CancellationToken cancellationToken)
    {
        try
        {
            using Process process = Process.Start(new ProcessStartInfo("git", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            })!;
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode == 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsCore(string name)
    {
        return name.StartsWith(CoreScope, StringComparison.Ordinal)
            || string.Equals(name, CoreMarketName, StringComparison.Ordinal);
    }

    private static void GuardThirdParty(string name)
    {
        if (IsCore(name))
        {
            throw new InvalidOperationException($"官方核心插件 {name} 不可变更（§18 恢复场景只动第三方）。");
        }
    }

    private JsonObject ReadManifest()
    {
        if (!File.Exists(ManifestPath))
        {
            throw new InvalidOperationException($"找不到 Profile 清单：{ManifestPath}");
        }

        return JsonNode.Parse(File.ReadAllText(ManifestPath)) as JsonObject
            ?? throw new InvalidOperationException($"Profile 清单不是 JSON 对象：{ManifestPath}");
    }

    private void WriteManifest(JsonObject manifest)
    {
        File.WriteAllText(ManifestPath, manifest.ToJsonString(IndentedJsonOptions));
    }

    private static readonly System.Text.Json.JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };

    private static HashSet<string> ReadBundles(JsonObject manifest)
    {
        HashSet<string> bundles = new(StringComparer.Ordinal);
        if (manifest["dsh"]?["profile"]?["bundles"] is JsonArray array)
        {
            foreach (JsonNode? node in array)
            {
                if (node is JsonValue value && value.TryGetValue(out string? text))
                {
                    bundles.Add(text);
                }
            }
        }

        return bundles;
    }

    private static JsonArray GetOrCreateBundlesArray(JsonObject manifest)
    {
        if (manifest["dsh"] is not JsonObject dsh)
        {
            dsh = new JsonObject();
            manifest["dsh"] = dsh;
        }

        if (dsh["profile"] is not JsonObject profile)
        {
            profile = new JsonObject();
            dsh["profile"] = profile;
        }

        if (profile["bundles"] is not JsonArray bundles)
        {
            bundles = new JsonArray();
            profile["bundles"] = bundles;
        }

        return bundles;
    }

    /// <summary>
    /// 读取已安装插件的版本与描述（node_modules/&lt;pkg&gt;/package.json 一次读；缺文件/缺字段给占位/空串）。
    /// </summary>
    private (string Version, string Description) ReadInstalledInfo(string name)
    {
        string packageJsonPath = Path.Combine(profileDir, "node_modules", name, "package.json");
        if (!ProfileBundleProbe.IsBundleResolved(profileDir, name))
        {
            return ("未安装", string.Empty);
        }

        JsonNode? node = JsonNode.Parse(File.ReadAllText(packageJsonPath));
        return (
            node?["version"]?.GetValue<string>() ?? "未知",
            node?["description"]?.GetValue<string>() ?? string.Empty);
    }

    private void CleanPatchEntries(string name)
    {
        if (!File.Exists(PatchPath))
        {
            return;
        }

        YamlStream stream = new();
        using (StreamReader reader = File.OpenText(PatchPath))
        {
            stream.Load(reader);
        }

        if (stream.Documents.Count == 0
            || stream.Documents[0].RootNode is not YamlSequenceNode root)
        {
            return;
        }

        int removed = 0;
        List<YamlNode> toRemove = [];
        foreach (YamlNode entry in root.Children)
        {
            if (entry is not YamlMappingNode mapping)
            {
                continue;
            }

            if (GetScalar(mapping, "name") == name)
            {
                toRemove.Add(entry);
                removed++;
                continue;
            }

            if (mapping.Children.TryGetValue(new YamlScalarNode("insert"), out YamlNode? insertNode)
                && insertNode is YamlSequenceNode insertList)
            {
                List<YamlNode> insertToRemove = insertList.Children
                    .Where(child => child is YamlMappingNode childMapping && GetScalar(childMapping, "name") == name)
                    .ToList();
                foreach (YamlNode child in insertToRemove)
                {
                    insertList.Children.Remove(child);
                    removed++;
                }

                if (insertList.Children.Count == 0 && GetScalar(mapping, "id") is null)
                {
                    toRemove.Add(entry);
                }
            }
        }

        foreach (YamlNode entry in toRemove)
        {
            root.Children.Remove(entry);
        }

        if (removed > 0)
        {
            using StreamWriter writer = File.CreateText(PatchPath);
            stream.Save(writer, assignAnchors: false);
        }
    }

    private static string? GetScalar(YamlMappingNode mapping, string key)
    {
        return mapping.Children.TryGetValue(new YamlScalarNode(key), out YamlNode? value)
            && value is YamlScalarNode scalar
                ? scalar.Value
                : null;
    }

    private async Task RebuildLockfileAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(pnpmCjsPath) || !File.Exists(pnpmCjsPath))
        {
            throw new InvalidOperationException(
                "找不到 vendored pnpm（pnpmCjsPath），无法重建 lockfile。请检查 dsh-desktop.config.json。");
        }

        // pnpm 写入前归一化清单（剥 BOM/代际投影/悬空 overrides）——卸载链路在此收口
        // （2026-09-19 实机：种子 profile 带指向 .generations 的悬空 link: overrides，
        // 卸载第④步未归一化直接 pnpm install 必失败，且①-③已生效留下半卸载现场）。
        ProfileManifestFixups.NormalizeToFlatModel(profileDir);

        // 离线优先策略收敛在 NodeJsToolRunner.InstallWithOfflineFallbackAsync（变更路径不在启动主路径）。
        (int exitCode, string outputTail) = await NodeJsToolRunner.InstallWithOfflineFallbackAsync(
            nodePath, pnpmCjsPath, profileDir, cancellationToken, _runOnce).ConfigureAwait(false);

        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"pnpm 重建 lockfile 失败（退出码 {exitCode}）。{outputTail}");
        }
    }
}
