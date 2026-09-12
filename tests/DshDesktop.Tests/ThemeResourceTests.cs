using System.Text.RegularExpressions;

namespace DshDesktop.Tests;

/// <summary>
/// 主题文件守卫：资源键存在性 + 动画属性可用性 + 页内共享状态的控件归属。
/// 这些都是 **XAML 编译器看不到** 的约束——编译能过，运行才炸。
/// </summary>
public sealed partial class ThemeResourceTests
{
    /// <summary>
    /// 外壳顶栏的行号（<c>MainWindow.axaml</c> 里 <c>Classes="topbar"</c> 所在行）。
    /// 页面内容从下一行开始，任何页内代码隐藏里的「页内共享状态」都必须在更靠后的行——
    /// 序号大于它就说明那个控件不在本页，属于复制粘贴来源的残留引用。
    /// </summary>
    private const int ShellTopbarLine = 182;

    [GeneratedRegex(@"\{(?:Dynamic|Static)Resource\s+([A-Za-z0-9_]+)\s*\}")]
    private static partial Regex ResourceReference();

    [GeneratedRegex("x:Key=\"([^\"]+)\"")]
    private static partial Regex ResourceDefinition();

    [Test]
    public async Task EveryReferencedThemeKey_IsDefinedInDshTheme()
    {
        var root = FindRepositoryRoot();
        await Assert.That(root).IsNotNull();

        var sources = EnumerateViews(root!).ToList();
        await Assert.That(sources).IsNotEmpty();

        // 定义可以出现在主题字典里，也可以出现在任何视图的局部 Resources 里——
        // 判据是「引用了但全仓没有任何地方定义」，而不是「没定义在 Themes/ 里」。
        var defined = sources
            .SelectMany(f => ResourceDefinition().Matches(File.ReadAllText(f)))
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        var missing = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in sources)
        {
            var text = await File.ReadAllTextAsync(file);
            foreach (Match m in ResourceReference().Matches(text))
            {
                var key = m.Groups[1].Value;
                if (IsThemed(key) && !defined.Contains(key))
                {
                    missing.Add($"{key}  ←  {Path.GetRelativePath(root!, file)}");
                }
            }
        }

        await Assert.That(string.Join(Environment.NewLine, missing)).IsEmpty();
    }

    private static bool IsThemed(string key)
    {
        return key.StartsWith("Dsh", StringComparison.Ordinal)
            || key.StartsWith("Icon", StringComparison.Ordinal);
    }

    [GeneratedRegex(
        "<StreamGeometry\\s+x:Key=\"([^\"]+)\"\\s*>(.*?)</StreamGeometry>",
        RegexOptions.Singleline)]
    private static partial Regex IconGeometryElement();

    [GeneratedRegex("<KeyFrame\\b[^>]*>([\\s\\S]*?)</KeyFrame>", RegexOptions.Singleline)]
    private static partial Regex KeyFrameElement();

    [Test]
    public async Task NoAnimationKeyframe_TargetsRenderTransform()
    {
        // RenderTransform 的声明类型是 ITransform，Avalonia 没有为它注册动画器，
        // 在 KeyFrame 里设它会抛 InvalidOperationException: No animator registered...
        // 更糟的是样式在**挂载阶段**就解析关键帧，与选择器是否命中该元素无关——
        // 一个只被某个页面用到的动画，足以把整个应用打死在启动之前（实测过）。
        //
        // 要动 transform，请动画具体分量：ScaleTransform.ScaleX 是 double，有动画器。
        var root = FindRepositoryRoot();
        await Assert.That(root).IsNotNull();

        var offenders = new List<string>();
        foreach (var file in EnumerateViews(root!))
        {
            foreach (Match frame in KeyFrameElement().Matches(File.ReadAllText(file)))
            {
                if (frame.Groups[1].Value.Contains("Property=\"RenderTransform\"", StringComparison.Ordinal))
                {
                    offenders.Add(Path.GetRelativePath(root!, file));
                }
            }
        }

        await Assert.That(string.Join(", ", offenders.Distinct())).IsEmpty();
    }

    [Test]
    public async Task EveryIconGeometry_HasPathData()
    {
        // 空的几何数据不会报错，只会让图标静默消失。这条守住「图标有轮廓可画」。
        var root = FindRepositoryRoot();
        await Assert.That(root).IsNotNull();

        var iconsFile = Path.Combine(
            root!, "src", "DshDesktop.Presentation.Avalonia", "Themes", "DshIcons.axaml");
        await Assert.That(File.Exists(iconsFile)).IsTrue();

        var text = await File.ReadAllTextAsync(iconsFile);
        var empty = IconGeometryElement()
            .Matches(text)
            .Where(m => string.IsNullOrWhiteSpace(m.Groups[2].Value))
            .Select(m => m.Groups[1].Value)
            .ToList();

        await Assert.That(string.Join(", ", empty)).IsEmpty();
    }

    [GeneratedRegex("Classes=\"([^\"]+)\"")]
    private static partial Regex ClassAttribute();

    [GeneratedRegex("Selector=\"([^\"]+)\"")]
    private static partial Regex StyleSelector();

    [GeneratedRegex("<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex XmlComment();

    [Test]
    public async Task EveryClassesMember_IsTargetedBySomeStyleSelector()
    {
        // Avalonia 对未知类名静默无视——写错一个字，控件只是"看起来有点怪"，不报错。
        // 这条守住「类名不是孤儿」。
        //
        // 注释里的示例不能当引用（`Classes="card*"` 出现在契约说明里，不是真控件）。
        //
        // 已知例外：布局类名（由 View 自己的容器提供样式，主题不负责渲染它们）。
        var layoutOnly = new HashSet<string>(StringComparer.Ordinal)
        {
            "brand", "nav-btn", "nav-label", "nav-badge", "topbar", "statusbar",
            "page-title", "page-desc", "runtime-mini-text",
        };

        var root = FindRepositoryRoot();
        await Assert.That(root).IsNotNull();

        var sources = EnumerateViews(root!).ToList();
        var targeted = sources
            .SelectMany(f => StyleSelector().Matches(File.ReadAllText(f)))
            .SelectMany(m => m.Groups[1].Value.Split(['.', ':', ' '], StringSplitOptions.RemoveEmptyEntries))
            .ToHashSet(StringComparer.Ordinal);

        var orphans = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in sources)
        {
            var text = XmlComment().Replace(await File.ReadAllTextAsync(file), string.Empty);
            foreach (Match m in ClassAttribute().Matches(text))
            {
                foreach (var cls in m.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!layoutOnly.Contains(cls) && !targeted.Contains(cls))
                    {
                        orphans.Add($"{cls}  ←  {Path.GetRelativePath(root!, file)}");
                    }
                }
            }
        }

        await Assert.That(string.Join(Environment.NewLine, orphans)).IsEmpty();
    }

    private static IEnumerable<string> EnumerateViews(string root)
    {
        var src = Path.Combine(root, "src");
        return Directory
            .EnumerateFiles(src, "*.axaml", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));
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
