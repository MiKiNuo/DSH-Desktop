using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia.Media;

namespace DshDesktop.Tests;

/// <summary>
/// 主题键的类型化守卫（候选 02），并覆盖「明暗双套画刷」的完整性。
///
/// 缺口：<c>ThemeResourceTests</c> 的正则匹配的是 XAML 标记扩展
/// <c>{DynamicResource X}</c>，**扫不到 C#**。因此
/// <c>RuntimeLifecycleBrushes.Resolve("DshOkBrush", ...)</c> 里写错键名、
/// 或主题字典删掉该键，既有测试全部保持绿色，运行时静默回退到硬编码色。
///
/// 本类补上两条射程：常量类与主题字典**双向**校验；明暗两套文件的**键集合必须一致**、
/// 关键键的**色值必须不同**（否则切主题对那个键毫无效果）。
/// </summary>
public sealed partial class ThemeKeyTests
{
    private const string ThemeKeysTypeName =
        "DshDesktop.Presentation.Avalonia.Themes.DshThemeKeys";

    private const string IconKeysTypeName =
        "DshDesktop.Presentation.Avalonia.Themes.DshIconKeys";

    private const string BrushesTypeName =
        "DshDesktop.Presentation.Avalonia.Features.Runtime.RuntimeLifecycleBrushes";

    /// <summary>
    /// 明暗两套画刷文件（<c>DshTheme.axaml</c> 的 ThemeDictionaries 各挂一个）。
    /// 顺序有意义：第 0 项是深色，<c>RuntimeLifecycleBrushes</c> 的回退常量对位的是它。
    /// </summary>
    private static readonly string[] ColorFileNames = ["DshColors.Dark.axaml", "DshColors.Light.axaml"];

    /// <summary>画刷定义之外的资源键宿主（字号 / 圆角 / 样式族）。</summary>
    private const string StyleFileName = "DshTheme.axaml";

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
        var defined = DefinedThemeKeys(root!)
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

        var themeKeys = DefinedThemeKeys(root!).ToHashSet(StringComparer.Ordinal);
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
    public async Task ThemeColorFiles_DefineIdenticalKeySets()
    {
        // 两套文件的键集合必须一致：只在亮色里定义的键，深色下取不到会静默沿用另一方的值，
        // 表现为「切回去以后某个控件还是浅色的」。反之亦然。
        var root = FindRepositoryRoot();
        await Assert.That(root).IsNotNull();

        // 用「文件里全部 x:Key」而非 ColorsOf（后者只匹配 SolidColorBrush）：
        // 否则 DshCardShadow 这类 BoxShadows 资源被排除在射程外，某套缺键也无人发现。
        var dark = DefinedKeys(root!, ColorFileNames[0]).ToHashSet(StringComparer.Ordinal);
        var light = DefinedKeys(root!, ColorFileNames[1]).ToHashSet(StringComparer.Ordinal);

        var asymmetric = dark
            .Where(k => !light.Contains(k))
            .Select(k => $"{ColorFileNames[1]} 缺 {k}")
            .Concat(light
                .Where(k => !dark.Contains(k))
                .Select(k => $"{ColorFileNames[0]} 缺 {k}"))
            .Order()
            .ToList();

        await Assert.That(string.Join(Environment.NewLine, asymmetric)).IsEmpty();
    }

    [Test]
    [Arguments("DshBgBrush")]
    [Arguments("DshSurface1Brush")]
    [Arguments("DshTextBrush")]
    [Arguments("DshScrimBrush")]
    public async Task ThemeColorFiles_KeyColorsDifferPerTheme(string key)
    {
        // 复制粘贴后忘改值 → 切主题对该键毫无效果。挑的是最显眼、绝无可能两套同值的几个键。
        var root = FindRepositoryRoot();
        await Assert.That(root).IsNotNull();

        var dark = ColorsOf(root!, ColorFileNames[0]);
        var light = ColorsOf(root!, ColorFileNames[1]);

        await Assert.That(dark.ContainsKey(key)).IsTrue();
        await Assert.That(light.ContainsKey(key)).IsTrue();

        await Assert.That(light[key]).IsNotEqualTo(dark[key]);
    }

    [Test]
    public async Task RuntimeLifecycleBrushes_ResolvedColorsMatchTheme()
    {
        // Resolve 的十六进制回退是"键名 + 颜色"双份事实。文件注释要求人工同步，
        // 这条把注释变成机器强制：**解析时实际用到的**回退色必须与深色套同键的颜色相等
        // （回退只在取不到应用字典时发生，而那时正是启动最早的构造期，主题尚未切换过）。
        //
        // 为什么读 RuntimeLifecycleBrushes.ResolvedFallbackHex 而不是画刷本身：
        // Avalonia 的属性 getter 会触发 Dispatcher.VerifyAccess，非 UI 线程直接抛异常，
        // 测试进程读不到 SolidColorBrush.Color。ResolvedFallbackHex 记录的是 Resolve 的**真实实参**，
        // 所以把 Resolve(键, 别的常量) 写错同样会被抓到——镜像一份色值则抓不到。
        var root = FindRepositoryRoot();
        await Assert.That(root).IsNotNull();

        var themeColors = ColorsOf(root!, ColorFileNames[0]);

        var brushes = System.Reflection.Assembly.Load("DshDesktop.Presentation.Avalonia")
            .GetType(BrushesTypeName, throwOnError: true)!;

        // 触碰每个画刷成员，逼它们各跑一次 Resolve（成员是计算属性，不存在静态构造期初始化；
        // 早先版本的字段初始化器就是调用点，改成属性后必须显式读一遍才会记录回退色）。
        _ = brushes.GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(IBrush))
            .Select(p => p.GetValue(null))
            .ToList();

        var resolved = (IReadOnlyDictionary<string, string>)brushes
            .GetField("ResolvedFallbackHex", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

        // 9 个画刷成员都会调到 Resolve；少记一条就说明调用点被删了，不能空转通过。
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

    /// <summary>全部主题键的宿主：两套画刷文件 + 样式文件。</summary>
    private static IEnumerable<string> DefinedThemeKeys(string root)
        => ColorFileNames.Append(StyleFileName).SelectMany(f => DefinedKeys(root, f));

    /// <summary>单个文件里的「画刷键 → 颜色」。文件内键唯一，故可直接 ToDictionary。</summary>
    private static Dictionary<string, string> ColorsOf(string root, string fileName)
        => SolidColorDefinition()
            .Matches(ReadStripped(root, fileName))
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value, StringComparer.Ordinal);

    private static IEnumerable<string> DefinedKeys(string root, string fileName)
        => KeyDefinition()
            .Matches(ReadStripped(root, fileName))
            .Select(m => m.Groups[1].Value);

    /// <summary>
    /// 读文件并**先剥离注释**再交给正则：颜色文件头的说明里含 <c>x:Key="X"</c> 这样的示例，
    /// 不剥离就会被当成真实键，让「两套键集合一致」出现凭空的差集。
    /// 正则匹配 XAML 文本时一律走这里。
    /// </summary>
    private static string ReadStripped(string root, string fileName)
        => XamlScan.StripComments(File.ReadAllText(ThemeFilePath(root, fileName)));

    private static string ThemeFilePath(string root, string fileName)
        => Path.Combine(root, "src", "DshDesktop.Presentation.Avalonia", "Themes", fileName);

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
