using System.Text.RegularExpressions;

namespace DshDesktop.Tests;

/// <summary>
/// 主题资源引用守卫：Avalonia 的 <c>{DynamicResource}</c> 找不到键时静默失败（不抛错、
/// 只是不生效），一次 77 键的色板重命名很容易留下拼错或漏改的引用，且不会有任何报错。
/// 此测试把「所有被引用的 Dsh*/Icon* 键都必须在 DshTheme.axaml 中定义」变成编译期外的硬约束。
/// </summary>
public sealed partial class ThemeResourceTests
{
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
