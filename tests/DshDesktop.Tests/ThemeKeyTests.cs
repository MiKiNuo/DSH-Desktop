using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia.Media;

namespace DshDesktop.Tests;

/// <summary>
/// 主题键的类型化守卫（候选 02）。
///
/// 缺口：<c>ThemeResourceTests</c> 的正则匹配的是 XAML 标记扩展
/// <c>{DynamicResource X}</c>，**扫不到 C#**。因此
/// <c>RuntimeLifecycleBrushes.Resolve("DshOkBrush", ...)</c> 里写错键名、
/// 或主题字典删掉该键，四条既有测试全部保持绿色，运行时静默回退到硬编码色。
///
/// 本类补上这条射程：常量类与主题字典**双向**校验，并锁死回退色与主题色一致。
/// </summary>
public sealed partial class ThemeKeyTests
{
    private const string ThemeKeysTypeName =
        "DshDesktop.Presentation.Avalonia.Themes.DshThemeKeys";

    private const string IconKeysTypeName =
        "DshDesktop.Presentation.Avalonia.Themes.DshIconKeys";

    private const string BrushesTypeName =
        "DshDesktop.Presentation.Avalonia.Features.Runtime.RuntimeLifecycleBrushes";

    [GeneratedRegex("x:Key=\"([A-Za-z0-9_]+)\"")]
    private static partial Regex KeyDefinition();

    [GeneratedRegex(
        "<SolidColorBrush\\s+x:Key=\"([A-Za-z0-9_]+)\"\\s+Color=\"(#[0-9A-Fa-f]{6,8})\"")]
    private static partial Regex SolidColorDefinition();

    [Test]
    public async Task EveryDefinedThemeKey_HasConstant()
    {
        // 主题字典里新增一个键，却忘了在常量类登记 —— 那个键在 C# 侧就仍是字符串。
        var root = FindRepositoryRoot();
        await Assert.That(root).IsNotNull();

        var declared = ConstantsOf(ThemeKeysTypeName).Values.ToHashSet(StringComparer.Ordinal);
        var defined = DefinedKeys(root!, "DshTheme.axaml")
            .Where(k => k.StartsWith("Dsh", StringComparison.Ordinal))
            .ToList();

        var missing = defined.Where(k => !declared.Contains(k)).Order().ToList();

        await Assert.That(string.Join(", ", missing)).IsEmpty();
    }

    [Test]
    public async Task EveryDefinedIconKey_HasConstant()
    {
        var root = FindRepositoryRoot();
        await Assert.That(root).IsNotNull();

        var declared = ConstantsOf(IconKeysTypeName).Values.ToHashSet(StringComparer.Ordinal);
        var defined = DefinedKeys(root!, "DshIcons.axaml");

        var missing = defined.Where(k => !declared.Contains(k)).Order().ToList();

        await Assert.That(string.Join(", ", missing)).IsEmpty();
    }

    [Test]
    public async Task EveryConstant_ResolvesToADefinedKey()
    {
        // 常量指向了一个字典里根本不存在的键 —— 反过来也不行。
        var root = FindRepositoryRoot();
        await Assert.That(root).IsNotNull();

        var themeKeys = DefinedKeys(root!, "DshTheme.axaml").ToHashSet(StringComparer.Ordinal);
        var iconKeys = DefinedKeys(root!, "DshIcons.axaml").ToHashSet(StringComparer.Ordinal);

        var dangling = ConstantsOf(ThemeKeysTypeName)
            .Where(p => !themeKeys.Contains(p.Value))
            .Select(p => $"{ThemeKeysTypeName}.{p.Key} → {p.Value}")
            .Concat(ConstantsOf(IconKeysTypeName)
                .Where(p => !iconKeys.Contains(p.Value))
                .Select(p => $"{IconKeysTypeName}.{p.Key} → {p.Value}"))
            .Order()
            .ToList();

        await Assert.That(string.Join(Environment.NewLine, dangling)).IsEmpty();
    }

    [Test]
    public async Task RuntimeLifecycleBrushes_ResolvedColorsMatchTheme()
    {
        // Resolve 的十六进制回退是"键名 + 颜色"双份事实。文件注释要求人工同步，
        // 这条把注释变成机器强制：**解析时实际用到的**回退色必须与 DshTheme.axaml 同键的颜色相等。
        //
        // 为什么读 RuntimeLifecycleBrushes.ResolvedFallbackHex 而不是画刷本身：
        // Avalonia 的属性 getter 会触发 Dispatcher.VerifyAccess，非 UI 线程直接抛异常，
        // 测试进程读不到 SolidColorBrush.Color。ResolvedFallbackHex 记录的是 Resolve 的**真实实参**，
        // 所以把 Resolve(键, 别的常量) 写错同样会被抓到——镜像一份色值则抓不到。
        var root = FindRepositoryRoot();
        await Assert.That(root).IsNotNull();

        var themeColors = SolidColorDefinition()
            .Matches(File.ReadAllText(Path.Combine(
                root!, "src", "DshDesktop.Presentation.Avalonia", "Themes", "DshTheme.axaml")))
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value, StringComparer.Ordinal);

        var brushes = System.Reflection.Assembly.Load("DshDesktop.Presentation.Avalonia")
            .GetType(BrushesTypeName, throwOnError: true)!;

        // 触碰该类型，确保静态初始化已跑完（字段初始化器即 Resolve 的调用点）。
        _ = brushes.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(IBrush))
            .Select(f => f.GetValue(null))
            .ToList();

        var resolved = (Dictionary<string, string>)brushes
            .GetField("ResolvedFallbackHex", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

        // 9 个画刷字段都调用了 Resolve；少记一条就说明调用点被删了，不能空转通过。
        const int ExpectedBrushCount = 9;
        await Assert.That(resolved.Count).IsEqualTo(ExpectedBrushCount);

        var mismatched = resolved
            .Where(pair => !themeColors.TryGetValue(pair.Key, out var expected)
                        || !string.Equals(
                            NormalizeHex(pair.Value), NormalizeHex(expected), StringComparison.OrdinalIgnoreCase))
            .Select(pair => $"{pair.Key}: 实际回退={pair.Value} 主题={(themeColors.TryGetValue(pair.Key, out var e) ? e : "<缺失>")}")
            .Order()
            .ToList();

        await Assert.That(string.Join(Environment.NewLine, mismatched)).IsEmpty();
    }

    /// <summary>把画刷的 <c>#AARRGGBB</c> 与 axaml 的 <c>#RRGGBB</c> / <c>#AARRGGBB</c> 归一到可比较形式。</summary>
    private static string NormalizeHex(string hex)
    {
        var v = hex.TrimStart('#');
        return v.Length == 6 ? v : v[2..];
    }

    private static IEnumerable<string> DefinedKeys(string root, string fileName)
    {
        var path = Path.Combine(
            root, "src", "DshDesktop.Presentation.Avalonia", "Themes", fileName);
        return KeyDefinition().Matches(File.ReadAllText(path)).Select(m => m.Groups[1].Value);
    }

    private static Dictionary<string, string> ConstantsOf(string typeName)
    {
        var type = Type.GetType(typeName + ", DshDesktop.Presentation.Avalonia");
        if (type is null)
        {
            return [];
        }

        return type
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue()!, StringComparer.Ordinal);
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
