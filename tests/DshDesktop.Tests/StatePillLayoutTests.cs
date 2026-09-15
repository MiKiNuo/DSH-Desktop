namespace DshDesktop.Tests;

/// <summary>
/// Runtime 状态机 pill（<c>Border.state</c>）文字居中守卫。
///
/// 背景（2026-09-15 实机投诉）：pill 是<b>固定高度</b>（Height=30）+ 上下 Padding=0 的 Border，
/// 内层 TextBlock 默认 <c>VerticalAlignment=Stretch</c> ⇒ 被拉伸到内容区全高（28 DIP），
/// 而 TextBlock 的文字始终画在自身顶部 ⇒ 字形整体上浮、视觉不居中。
/// 这类缺陷 XAML 编译器完全看不见（改回去照过），沙箱也无法截图 Avalonia 窗口，
/// 故由本测试固化契约。
/// </summary>
public sealed class StatePillLayoutTests
{
    /// <summary>
    /// pill 文字必须垂直居中：全局样式 <c>Border.state TextBlock</c> 须显式声明
    /// <c>VerticalAlignment=Center</c>（让 TextBlock 收缩到字形高度、再由父级居中）。
    /// 样式层修复可一次覆盖全部 6 个 pill，避免每个调用点各写一遍。
    /// </summary>
    [Test]
    public async Task StatePillText_IsVerticallyCentered()
    {
        var text = XamlScan.StripComments(
            await ReadAsync("DshDesktop.Presentation.Avalonia", "Themes", "DshTheme.axaml"));

        var block = XamlScan.ExtractStyleBlock(text, "Border.state TextBlock");
        await Assert.That(block).IsNotEmpty();

        // Setter 语法拆两段：Property="VerticalAlignment" 与 Value="Center"。
        await Assert.That(block).Contains("Property=\"VerticalAlignment\"");
        await Assert.That(block).Contains("Value=\"Center\"");
    }

    private static async Task<string> ReadAsync(params string[] relativeSegments)
    {
        var root = XamlScan.FindRepositoryRoot();
        await Assert.That(root).IsNotNull();

        var path = Path.Combine(new[] { root!, "src" }.Concat(relativeSegments).ToArray());
        await Assert.That(File.Exists(path)).IsTrue();
        return await File.ReadAllTextAsync(path);
    }
}
