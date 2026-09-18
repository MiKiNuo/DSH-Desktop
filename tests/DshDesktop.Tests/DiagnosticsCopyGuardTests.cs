namespace DshDesktop.Tests;

/// <summary>
/// 诊断中心右键复制守卫（2026-09-17）：
/// 日志行是普通 <c>TextBlock</c>（不可选中）、列表项被主题改成透明自绘行，因此「拷贝报错日志」只能靠右键菜单。
/// 测试项目不引用 <c>DshDesktop.App</c>，无法运行时断言右键行为，故按仓内既有惯例
/// （ViewUiThreadGuardTests / ToastUiThreadGuardTests）读源码文本固化三条不变量：
/// ① 菜单挂在 <c>EntriesList</c> 上（单个实例，而非 DataTemplate 内每行一份）；
/// ② 目标行经 <c>ContextRequested</c> 从命中元素向上解出（不依赖弹层的 DataContext 继承）；
/// ③ 复制文本一律经 <c>DiagnosticCopyText</c> 组装后写系统剪贴板。
/// 缺任一条即功能静默失效，测试即红。
/// </summary>
public sealed class DiagnosticsCopyGuardTests
{
    private const string CopyRowHandler = "复制此条";
    private const string CopySelectedHandler = "复制选中行";
    private const string CopyAllHandler = "复制当前筛选全部";

    [Test]
    public async Task DiagnosticsView_DeclaresCopyMenuOnEntriesList()
    {
        var xaml = await ReadViewAsync();

        var listStart = xaml.IndexOf("<ListBox x:Name=\"EntriesList\"", StringComparison.Ordinal);
        var menuStart = xaml.IndexOf("<ListBox.ContextMenu>", StringComparison.Ordinal);
        var templateStart = xaml.IndexOf("<ListBox.ItemTemplate>", StringComparison.Ordinal);
        var menuEnd = xaml.IndexOf("</ListBox.ContextMenu>", StringComparison.Ordinal);

        // 菜单必须声明在 ListBox 上、且在 ItemTemplate 之前（= ListBox 的属性元素，不是每行一份）。
        await Assert.That(listStart).IsGreaterThan(-1);
        await Assert.That(menuStart).IsGreaterThan(listStart);
        await Assert.That(menuEnd).IsGreaterThan(menuStart);
        await Assert.That(templateStart).IsGreaterThan(menuEnd);

        // 断言「Header 紧邻 Click」的整段：分开断言时把两个处理器对调仍会全绿。
        await Assert.That(xaml).Contains($"Header=\"{CopyRowHandler}\" Click=\"OnCopyRowClicked\"");
        await Assert.That(xaml).Contains($"Header=\"{CopySelectedHandler}\" Click=\"OnCopySelectedClicked\"");
        await Assert.That(xaml).Contains($"Header=\"{CopyAllHandler}\" Click=\"OnCopyAllClicked\"");
    }

    [Test]
    public async Task DiagnosticsView_EnablesMultiSelection()
    {
        // 多选是左键拖选 / Shift 连选 / Ctrl 点选的前提，缺失时整组交互静默退回单选。
        // Avalonia 12 已移除 Extended，Multiple 即涵盖 Ctrl/Shift 语义。
        var xaml = await ReadViewAsync();

        await Assert.That(xaml).Contains("<ListBox x:Name=\"EntriesList\"");
        await Assert.That(xaml).Contains("SelectionMode=\"Multiple\"");
    }

    [Test]
    public async Task DiagnosticsView_CtrlCCopiesSelectedRows()
    {
        var code = await ReadCodeBehindAsync();

        // Ctrl+C 与右键「复制选中行」都必须落到同一个选中集复制路径。
        await Assert.That(code).Contains("KeyDown += OnEntriesListKeyDown");
        await Assert.That(code).Contains("Key.C");
        await Assert.That(code).Contains("KeyModifiers.Control");
        await Assert.That(code).Contains("_entriesList.SelectedItems");
        await Assert.That(code).Contains("CopySelectedRowsAsync(");
        await Assert.That(code).Contains("await CopySelectedRowsAsync()");
    }

    [Test]
    public async Task DiagnosticsView_PreservesSelectionAcrossSyncRows()
    {
        var code = await ReadCodeBehindAsync();

        // SyncRows 每条新日志都会 Clear 重建所有行，选择随之清空；
        // 必须按源事件身份（DiagnosticRow.Event）在重建后恢复选中，否则 Live 流水中多选形同虚设。
        await Assert.That(code).Contains("selectedEvents");
        await Assert.That(code).Contains("row => row.Event");
        await Assert.That(code).Contains("_entriesList.SelectedItems.Add(");
    }

    [Test]
    public async Task DiagnosticsView_ResolvesTargetRowFromContextRequested()
    {
        var code = await ReadCodeBehindAsync();

        await Assert.That(code).Contains("ContextRequested += OnContextRequested");
        await Assert.That(code).Contains("FindAncestorOfType<ListBoxItem>");
        await Assert.That(code).Contains("as DiagnosticRow");

        // 列表空白区/键盘唤起菜单时命中不到行，必须回落到当前选中行。
        await Assert.That(code).Contains("SelectedItem as DiagnosticRow");
    }

    [Test]
    public async Task DiagnosticsView_WritesClipboardViaCopyText()
    {
        var code = await ReadCodeBehindAsync();

        await Assert.That(code).Contains("TopLevel.GetTopLevel(this)");
        await Assert.That(code).Contains("SetTextAsync(");

        // 钉死调用点（而不只是「两个串各自出现过」）：拼好的文本必须真的被送进剪贴板。
        await Assert.That(code).Contains("await CopyToClipboardAsync(DiagnosticCopyText.ForRow(");
        await Assert.That(code).Contains("await CopyToClipboardAsync(DiagnosticCopyText.ForRows(");
    }

    private static async Task<string> ReadViewAsync()
    {
        var root = XamlScan.FindRepositoryRoot();
        await Assert.That(root).IsNotNull();

        var path = Path.Combine(
            root!, "src", "DshDesktop.Presentation.Avalonia", "Features", "Diagnostics", "DiagnosticsView.axaml");
        await Assert.That(File.Exists(path)).IsTrue();

        return XamlScan.StripComments(await File.ReadAllTextAsync(path));
    }

    private static async Task<string> ReadCodeBehindAsync()
    {
        var root = XamlScan.FindRepositoryRoot();
        await Assert.That(root).IsNotNull();

        var path = Path.Combine(
            root!, "src", "DshDesktop.Presentation.Avalonia", "Features", "Diagnostics", "DiagnosticsView.axaml.cs");
        await Assert.That(File.Exists(path)).IsTrue();

        return XamlScan.StripComments(await File.ReadAllTextAsync(path));
    }
}
