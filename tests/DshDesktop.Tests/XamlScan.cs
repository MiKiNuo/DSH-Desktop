using System.Text.RegularExpressions;

namespace DshDesktop.Tests;

/// <summary>
/// XAML 视图扫描共享基建：仓库根定位、视图文件枚举、注释剥离。
/// 供 ThemeResourceTests / NoticeLayoutTests 等结构性 XAML 守卫测试共用，
/// 避免每个测试类各抄一份（code-review 2026-09-13 发现的重复）。
/// </summary>
internal static partial class XamlScan
{
    [GeneratedRegex("<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex XmlComment();

    [GeneratedRegex("<Style\\s+Selector=\"([^\"]+)\"\\s*>")]
    private static partial Regex StyleHeader();

    /// <summary>剥离 XAML 注释，防止注释里的示例文本被误判为控件引用。</summary>
    public static string StripComments(string text) => XmlComment().Replace(text, string.Empty);

    /// <summary>取出某个 <c>&lt;Style Selector="..."&gt;</c> 块的正文（不含结束标签）；未找到返回空串。</summary>
    public static string ExtractStyleBlock(string text, string selector)
    {
        var match = StyleHeader().Matches(text)
            .FirstOrDefault(m => m.Groups[1].Value == selector);

        if (match is null)
        {
            return string.Empty;
        }

        var end = text.IndexOf("</Style>", match.Index, StringComparison.Ordinal);
        return end < 0 ? string.Empty : text[match.Index..end];
    }

    /// <summary>枚举 src 下全部视图（排除 bin/obj 编译产物）。</summary>
    public static IEnumerable<string> EnumerateViews(string root)
    {
        var src = Path.Combine(root, "src");
        return Directory
            .EnumerateFiles(src, "*.axaml", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));
    }

    /// <summary>从测试程序集目录向上定位仓库根（以 DshDesktop.slnx 为标志）。</summary>
    public static string? FindRepositoryRoot()
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
