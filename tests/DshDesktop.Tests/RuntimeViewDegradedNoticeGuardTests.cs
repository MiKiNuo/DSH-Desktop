namespace DshDesktop.Tests;

/// <summary>
/// Runtime 页「插件未激活」常驻提示的结构守卫。
///
/// 背景（2026-09-28 实机）：dsh runtime 升到 0.1.7-rc.2 后，typert-loader 的新硬校验让手写 typert
/// manifest 的插件（dsh-myrules@0.1.1）注册失败，并把整个 loader 插件的注册连带回滚 ⇒ api-gateway
/// 拒绝客户端全部远程调用 ⇒ **工作台整页空白**；而 Runtime 进程、HTTP 探测、WebView 导航全部正常，
/// 宿主此前没有任何可见信号。这一条提示是该故障唯一的用户侧线索，故由测试固化：
/// ① 绑定名不得写错（写错只是静默不显示，编译器不报错）；
/// ② 必须挂在 <c>notice err</c> 上（工作台不可用属于错误级，且复用全仓既有 notice 契约，不新增样式）。
///
/// 断言前先剥注释并**折叠全部空白**（同 DiagnosticsConsoleScrollGuardTests），
/// 故期望串是不含任何空格的折叠形态：`notice err` → `noticeerr`、`{Binding X}` → `{BindingX}`。
/// 局限：结构性守卫，不驱动真实绑定求值；行为级锁死需 Avalonia.Headless 基建（测试项目现无）。
/// </summary>
public sealed class RuntimeViewDegradedNoticeGuardTests
{
    /// <summary>
    /// 提示条本体：err 变体 + 可见性绑到 HasDegradedPlugins 投影，且两者在同一开标签内相邻。
    /// </summary>
    [Test]
    public async Task RuntimeView_HasDegradedPluginsNotice()
    {
        string xaml = await ReadCompactedViewAsync();

        await Assert.That(xaml.Contains(
            "<BorderClasses=\"noticeerr\"IsVisible=\"{BindingHasDegradedPlugins}\">",
            StringComparison.Ordinal)).IsTrue();
    }

    /// <summary>
    /// 标题与正文必须分别绑到标题常量与 DegradedPluginsText 投影：
    /// 只留一个绑定（或两处错绑同一个）时，用户要么看不到原因、要么看不到肇事插件名。
    /// </summary>
    [Test]
    public async Task RuntimeView_DegradedNotice_BindsTitleAndPluginList()
    {
        string xaml = await ReadCompactedViewAsync();

        await Assert.That(xaml.Contains(
            "Classes=\"notice-title\"Text=\"插件未激活·工作台可能无法显示\"",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(xaml.Contains(
            "<TextBlockText=\"{BindingDegradedPluginsText}\"",
            StringComparison.Ordinal)).IsTrue();
    }

    /// <summary>
    /// 读取 Runtime 页 XAML，剥注释后**折叠全部空白**：断言因此不受换行符（本仓源文件 LF，签出可能变 CRLF）、
    /// 缩进与后续重排影响。文件缺失即断言失败，避免守卫静默变成永远通过。
    /// </summary>
    private static async Task<string> ReadCompactedViewAsync()
    {
        string? root = XamlScan.FindRepositoryRoot();
        await Assert.That(root).IsNotNull();

        string path = Path.Combine(
            root!,
            "src",
            "DshDesktop.Presentation.Avalonia",
            "Features",
            "Runtime",
            "RuntimeView.axaml");
        await Assert.That(File.Exists(path)).IsTrue();

        string text = XamlScan.StripComments(await File.ReadAllTextAsync(path));
        return string.Concat(text.Where(static c => !char.IsWhiteSpace(c)));
    }
}
