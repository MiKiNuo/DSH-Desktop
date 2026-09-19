namespace DshDesktop.Tests;

/// <summary>
/// 应用图标守卫：桌面快捷方式 / 开始菜单 / 卸载条目的图标全部来自 exe 内嵌图标，
/// 而 exe 内嵌图标唯一来源是 csproj 的 ApplicationIcon。三处锚点（csproj 引用、
/// ico 实体、iss SetupIconFile）任一失守，发布产物即退回 .NET / Inno 默认图标。
/// </summary>
public class AppIconGuardTests
{
    [Test]
    public async Task AppCsproj_ReferencesApplicationIcon()
    {
        string? root = XamlScan.FindRepositoryRoot();
        await Assert.That(root).IsNotNull();

        string csprojPath = Path.Combine(root!, "src", "DshDesktop.App", "DshDesktop.App.csproj");
        string csproj = (await File.ReadAllTextAsync(csprojPath)).Replace("\r\n", "\n");

        await Assert.That(csproj.Contains("<ApplicationIcon>Assets\\app.ico</ApplicationIcon>", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task AppIco_ExistsAndContainsRequiredSizes()
    {
        string? root = XamlScan.FindRepositoryRoot();
        await Assert.That(root).IsNotNull();

        string icoPath = Path.Combine(root!, "src", "DshDesktop.App", "Assets", "app.ico");
        await Assert.That(File.Exists(icoPath)).IsTrue();

        byte[] bytes = await File.ReadAllBytesAsync(icoPath);
        // ICO 头：reserved=0, type=1, count>=1
        await Assert.That(bytes.Length).IsGreaterThan(6);
        await Assert.That(bytes[0]).IsEqualTo((byte)0);
        await Assert.That(bytes[1]).IsEqualTo((byte)0);
        await Assert.That(bytes[2]).IsEqualTo((byte)1);
        await Assert.That(bytes[3]).IsEqualTo((byte)0);
        int count = bytes[4] | (bytes[5] << 8);
        await Assert.That(count).IsGreaterThan(0);

        // 目录项 16 字节：首字节宽（0 表示 256）。必须覆盖 16（小图标）与 256（大图标/高分屏）。
        var sizes = new HashSet<int>();
        for (int i = 0; i < count; i++)
        {
            int width = bytes[6 + i * 16];
            sizes.Add(width == 0 ? 256 : width);
        }
        await Assert.That(sizes.Contains(16)).IsTrue();
        await Assert.That(sizes.Contains(256)).IsTrue();
    }

    [Test]
    public async Task InstallerScript_UsesSetupIconFile()
    {
        // 安装程序自身图标：SetupIconFile 指向仓库内 app.ico（相对脚本目录解析）。
        string? root = XamlScan.FindRepositoryRoot();
        await Assert.That(root).IsNotNull();

        string issPath = Path.Combine(root!, "installer", "DshDesktop.iss");
        string iss = (await File.ReadAllTextAsync(issPath)).Replace("\r\n", "\n");

        await Assert.That(iss.Contains("SetupIconFile=", StringComparison.Ordinal)).IsTrue();
        await Assert.That(iss.Contains("app.ico", StringComparison.Ordinal)).IsTrue();
    }
}
