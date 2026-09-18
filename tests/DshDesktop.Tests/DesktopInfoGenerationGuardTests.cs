using System.Text.RegularExpressions;

namespace DshDesktop.Tests;

/// <summary>
/// DesktopInfo.g.cs 生成守卫（Presentation.Avalonia.csproj 为 MSBuild 脚本，只能文本断言）：
/// WriteLinesToFile 的 Lines 属性是 MSBuild 项列表，按未转义 ';' 切分为多行。
/// 历史缺陷：注释里 "-p:Version; local:" 的裸分号把一行注释拆成两行，生成文件第 4 行
/// "local: Directory.Build.props)." 触发 CS0116/CS1022，本地增量构建不触发该 target 而不红，
/// CI 干净检出必红（v0.1.1 发布失败）。合法分号必须写作 %3B；&#x..;/&#..; 数字字符引用亦合法。
/// </summary>
public sealed class DesktopInfoGenerationGuardTests
{
    [Test]
    public async Task WriteLinesToFile_LinesAttribute_HasNoRawSemicolon()
    {
        string? root = XamlScan.FindRepositoryRoot();
        await Assert.That(root).IsNotNull();

        string path = Path.Combine(root!, "src", "DshDesktop.Presentation.Avalonia", "DshDesktop.Presentation.Avalonia.csproj");
        await Assert.That(File.Exists(path)).IsTrue();
        string source = (await File.ReadAllTextAsync(path)).Replace("\r\n", "\n");

        const string anchor = "Lines=\"";
        int start = source.IndexOf(anchor, StringComparison.Ordinal);
        await Assert.That(start >= 0).IsTrue();

        int valueStart = start + anchor.Length;
        int valueEnd = source.IndexOf('"', valueStart);
        await Assert.That(valueEnd > valueStart).IsTrue();
        string lines = source[valueStart..valueEnd];

        // 剥离合法转义后再判裸分号：%3B（MSBuild 项内分号转义）、XML 数字字符引用与命名实体（&quot; 等）。
        string stripped = Regex.Replace(lines, "%3[Bb]|&#x[0-9A-Fa-f]+;|&#[0-9]+;|&[A-Za-z]+;", string.Empty);
        await Assert.That(stripped.Contains(';', StringComparison.Ordinal)).IsFalse();
    }
}
