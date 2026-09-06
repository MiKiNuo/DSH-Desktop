using DshDesktop.Application.Startup;

namespace DshDesktop.Tests;

/// <summary>
/// 开机自启编排测试（Phase 8 Issue 05）：注册表 IO 走 <see cref="IStartupRegistrar"/> 端口，
/// 测试以 Fake 验证开关状态与当前 exe 路径的转发（未安装形态同样写入当前 exe 路径）。
/// </summary>
public sealed class StartupRegistrationServiceTests
{
    [Test]
    public async Task Enable_WritesCurrentExecutablePath()
    {
        var registrar = new FakeStartupRegistrar();
        var service = new StartupRegistrationService(registrar, () => @"D:\Apps\DshDesktop.exe");

        service.SetEnabled(true);

        await Assert.That(registrar.Calls.Count).IsEqualTo(1);
        await Assert.That(registrar.Calls[0].Enabled).IsTrue();
        await Assert.That(registrar.Calls[0].ExecutablePath).IsEqualTo(@"D:\Apps\DshDesktop.exe");
    }

    [Test]
    public async Task Disable_ForwardsRemoval()
    {
        var registrar = new FakeStartupRegistrar();
        var service = new StartupRegistrationService(registrar, () => @"D:\Apps\DshDesktop.exe");

        service.SetEnabled(false);

        await Assert.That(registrar.Calls.Count).IsEqualTo(1);
        await Assert.That(registrar.Calls[0].Enabled).IsFalse();
    }

    /// <summary>
    /// 表示开机自启注册端口的测试替身（替代 HKCU Run 键真实写入）。
    /// </summary>
    private sealed class FakeStartupRegistrar : IStartupRegistrar
    {
        public List<(bool Enabled, string ExecutablePath)> Calls { get; } = [];

        public void SetEnabled(bool enabled, string executablePath)
        {
            Calls.Add((enabled, executablePath));
        }
    }
}
