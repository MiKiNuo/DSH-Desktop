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
    string? pnpmCjsPath) : IPluginManager
{
    private const string CoreScope = "@deepseek-ai/";
    private const string CoreMarketName = "dshmarket";

    private string ManifestPath => Path.Combine(profileDir, "package.json");
    private string PatchPath => Path.Combine(profileDir, "cordis.patch.yml");

    /// <inheritdoc />
    public Task<IReadOnlyList<PluginInfo>> ListPluginsAsync(CancellationToken cancellationToken)
    {
        JsonObject manifest = ReadManifest();
        HashSet<string> bundles = ReadBundles(manifest);
        List<PluginInfo> plugins = [];

        if (manifest["dependencies"] is JsonObject dependencies)
        {
            foreach ((string name, JsonNode? _) in dependencies)
            {
                (string version, string description) = ReadInstalledInfo(name);
                plugins.Add(new PluginInfo(
                    name,
                    version,
                    IsCore(name),
                    bundles.Contains(name),
                    description));
            }
        }

        return Task.FromResult<IReadOnlyList<PluginInfo>>(plugins);
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

        // 本地文件（.tgz / 目录）走 --offline；registry 包名允许联网。
        bool isLocalSource = File.Exists(source) || Directory.Exists(source);
        string[] arguments = isLocalSource
            ? ["add", source, "--offline"]
            : ["add", source];

        (int exitCode, string outputTail) = await NodeJsToolRunner.RunAsync(
            nodePath, pnpmCjsPath!, profileDir, arguments, cancellationToken).ConfigureAwait(false);
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

        // 校验是 DSH 插件（声明 dsh.bundle.patch），不是则抛错交给事务回滚。
        string installedManifestPath = Path.Combine(profileDir, "node_modules", pluginName, "package.json");
        JsonNode? installedManifest = File.Exists(installedManifestPath)
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

        return pluginName;
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

    private static string ResolveNameFromSource(string source)
    {
        // "name@version" / "@scope/name@version" / 本地路径的文件名。
        string candidate = File.Exists(source) || Directory.Exists(source)
            ? Path.GetFileNameWithoutExtension(source.TrimEnd(Path.DirectorySeparatorChar))
            : source;
        int atIndex = candidate.LastIndexOf('@');
        return atIndex > 0 ? candidate[..atIndex] : candidate;
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
        if (!File.Exists(packageJsonPath))
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

        // 优先 --offline（§34 Offline First）；失败后允许联网重试一次（变更路径不在启动主路径）。
        (int exitCode, string outputTail) = await NodeJsToolRunner.RunAsync(
            nodePath, pnpmCjsPath, profileDir,
            ["install", "--no-frozen-lockfile", "--offline"], cancellationToken).ConfigureAwait(false);
        if (exitCode != 0)
        {
            (exitCode, outputTail) = await NodeJsToolRunner.RunAsync(
                nodePath, pnpmCjsPath, profileDir,
                ["install", "--no-frozen-lockfile"], cancellationToken).ConfigureAwait(false);
        }

        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"pnpm 重建 lockfile 失败（退出码 {exitCode}）。{outputTail}");
        }
    }
}
