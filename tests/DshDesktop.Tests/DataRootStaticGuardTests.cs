using DshDesktop.Infrastructure.Config;

namespace DshDesktop.Tests;

/// <summary>
/// 静态数据根守卫（独立评审发现）：DataRoot 静态初始化器引用声明在后的静态字段时，
/// C# 按文本顺序初始化导致读到 null，未安装形态首次访问即 TypeInitializationException——
/// 纯函数直测全部路过静态属性，此缺口在全套绿下漏网。测试进程（dotnet run 形态，
/// 必走兜底路径）直接访问静态属性即覆盖该回归。
/// </summary>
public sealed class DataRootStaticGuardTests
{
    [Test]
    public async Task DataRoot_StaticAccess_IsNotEmpty()
    {
        await Assert.That(DshDesktopConfigStore.DataRoot.Length > 0).IsTrue();
    }

    [Test]
    public async Task ConfigPath_StaticAccess_IsUnderDataRoot()
    {
        await Assert.That(
            DshDesktopConfigStore.ConfigPath.StartsWith(
                DshDesktopConfigStore.DataRoot, StringComparison.OrdinalIgnoreCase)).IsTrue();
    }
}
