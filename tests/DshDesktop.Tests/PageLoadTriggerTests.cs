namespace DshDesktop.Tests;

/// <summary>
/// 页面级加载触发权归属守卫（候选 03，架构文档 §5 规则 1/6）。
///
/// 规则：「View 只能产生 Intent」「View 不直接修改 State」。主窗口（壳 View）曾在
/// <c>RenderCurrentPage()</c> 里直接 <c>FeatureStore.DispatchAsync(LoadXxx)</c>——
/// 壳越权驱动 Feature 的状态变更。修复后：
/// <list type="bullet">
/// <item>触发权在各 Feature 自己的 View（<c>OnBind</c> 时执行本 Feature 的加载命令）；</item>
/// <item>主窗口对任何 Feature Store 不再有 DispatchAsync 调用。</item>
/// </list>
/// 加载行为不变：<c>CreateView</c> 每次导航新建 View，<c>OnBind</c> 每次执行，
/// 等价于旧「进入页面即加载」。
/// </summary>
public sealed class PageLoadTriggerTests
{
    [Test]
    public async Task MainWindow_DoesNotDispatchFeatureIntents()
    {
        // 壳只负责建视图与壳级指示；触发 Feature 数据加载不是壳的职责（§5 规则 1/6）。
        // toast 订阅（States.Subscribe）是另一条候选 03b 的范围，不在此处断言。
        var source = await ReadSourceAsync("src", "DshDesktop.App", "MainWindow.axaml.cs");

        await Assert.That(source.Contains("DispatchAsync(", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task PluginsView_TriggersLoadOnBind()
    {
        // 每次导航新建 View，OnBind 每次执行 → 每次进页刷新清单（与旧行为一致）。
        // 断言收窄到 OnBind 方法体内：文件里别处（如点击处理器）出现同名调用不算数。
        var source = await ReadSourceAsync(
            "src", "DshDesktop.Presentation.Avalonia", "Features", "Plugins", "PluginsView.axaml.cs");

        var body = await OnBindBodyAsync(source);
        await Assert.That(body.Contains("LoadPluginsCommand.Execute", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task SettingsView_TriggersLoadOnBind()
    {
        var source = await ReadSourceAsync(
            "src", "DshDesktop.Presentation.Avalonia", "Features", "Settings", "SettingsView.axaml.cs");

        var body = await OnBindBodyAsync(source);
        await Assert.That(body.Contains("LoadSettingsCommand.Execute", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task OnBindBodyAsync_HandlesCrLfNewlines()
    {
        // 回归：CI 的 Windows runner 用 core.autocrlf=true 把源文件检出为 CRLF。
        // 此时方法收尾只有 "\r\n    }\r\n"，原锚点 "\n    }\n" 匹配不到（end == -1）。
        // 本方法必须能在 CRLF 换行下切出 OnBind 方法体。
        var source = string.Join("\r\n", new[]
        {
            "namespace X;",
            "public class V",
            "{",
            "    protected override void OnBind()",
            "    {",
            "        __CR_LF_MARKER__LoadPluginsCommand.Execute(null);",
            "    }",
            "}",
        });

        var body = await OnBindBodyAsync(source);
        await Assert.That(body.Contains("__CR_LF_MARKER__LoadPluginsCommand.Execute", StringComparison.Ordinal)).IsTrue();
    }

    /// <summary>
    /// 切出 <c>OnBind</c> 方法体（从声明到 4 空格缩进的方法收尾大括号；方法内的 lambda
    /// 缩进更深，不会误切）。
    /// </summary>
    private static async Task<string> OnBindBodyAsync(string source)
    {
        // CI 的 Windows runner 用 core.autocrlf=true 把源文件检出为 CRLF，
        // 方法收尾是 "\r\n    }\r\n" 而非 "\n    }\n"，归一化后锚点才能匹配。
        source = source.Replace("\r\n", "\n");

        const string marker = "protected override void OnBind";
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        await Assert.That(start >= 0).IsTrue();

        var end = source.IndexOf("\n    }\n", start + marker.Length, StringComparison.Ordinal);
        await Assert.That(end >= 0).IsTrue();
        return source[start..end];
    }

    private static async Task<string> ReadSourceAsync(params string[] relativeParts)
    {
        var root = FindRepositoryRoot();
        await Assert.That(root).IsNotNull();
        var path = Path.Combine(new[] { root! }.Concat(relativeParts).ToArray());
        await Assert.That(File.Exists(path)).IsTrue();
        return await File.ReadAllTextAsync(path);
    }

    private static string? FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DshDesktop.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
