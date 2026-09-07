using DshDesktop.Presentation.Avalonia.Features.Workbench.Bridge;

namespace DshDesktop.Tests;

/// <summary>
/// Desktop Bridge 协议纯逻辑测试（ADR-0006）：请求/响应按 id 配对、取消→null、异常→reject、
/// 重复 id 拒绝、乱序响应按 id 各归各位、导航重置清空挂起表。
/// </summary>
public sealed class DesktopBridgeProtocolTests
{
    [Test]
    public async Task Parse_ValidPickRequest_ReturnsId()
    {
        var protocol = new DesktopBridgeProtocol();

        bool ok = protocol.TryParseRequest("""{"id":"dsh-pick-1","type":"pick"}""", out string id);

        await Assert.That(ok).IsTrue();
        await Assert.That(id).IsEqualTo("dsh-pick-1");
    }

    [Test]
    public async Task Parse_NonPickType_Rejected()
    {
        var protocol = new DesktopBridgeProtocol();

        bool ok = protocol.TryParseRequest("""{"id":"dsh-pick-1","type":"other"}""", out _);

        await Assert.That(ok).IsFalse();
    }

    [Test]
    public async Task Parse_GarbageBody_Rejected()
    {
        var protocol = new DesktopBridgeProtocol();

        await Assert.That(protocol.TryParseRequest("not json", out _)).IsFalse();
        await Assert.That(protocol.TryParseRequest(null, out _)).IsFalse();
        await Assert.That(protocol.TryParseRequest("""{"type":"pick"}""", out _)).IsFalse();
    }

    [Test]
    public async Task Pairing_NormalFlow_BeginThenComplete()
    {
        var protocol = new DesktopBridgeProtocol();

        await Assert.That(protocol.TryBegin("dsh-pick-1")).IsTrue();
        await Assert.That(protocol.PendingCount).IsEqualTo(1);

        protocol.Complete("dsh-pick-1");

        await Assert.That(protocol.PendingCount).IsEqualTo(0);
    }

    [Test]
    public async Task Pairing_DuplicateId_Rejected()
    {
        var protocol = new DesktopBridgeProtocol();

        await Assert.That(protocol.TryBegin("dsh-pick-1")).IsTrue();
        await Assert.That(protocol.TryBegin("dsh-pick-1")).IsFalse();
        await Assert.That(protocol.PendingCount).IsEqualTo(1);
    }

    [Test]
    public async Task Pairing_OutOfOrderResponses_MatchedById()
    {
        var protocol = new DesktopBridgeProtocol();

        protocol.TryBegin("dsh-pick-1");
        protocol.TryBegin("dsh-pick-2");

        // 后到的请求先完成（原生框返回顺序不保证与发起顺序一致）：按 id 配对，互不影响。
        protocol.Complete("dsh-pick-2");
        await Assert.That(protocol.PendingCount).IsEqualTo(1);

        protocol.Complete("dsh-pick-1");
        await Assert.That(protocol.PendingCount).IsEqualTo(0);
    }

    [Test]
    public async Task Complete_UnknownId_IsNoOp()
    {
        var protocol = new DesktopBridgeProtocol();

        protocol.Complete("never-begun");

        await Assert.That(protocol.PendingCount).IsEqualTo(0);
    }

    [Test]
    public async Task Reset_ClearsPending_SoReusedIdAccepted()
    {
        var protocol = new DesktopBridgeProtocol();

        protocol.TryBegin("dsh-pick-1");
        protocol.Reset();

        // 导航后页面上下文重建，shim 序号归 0：挂起表必须同步清空，否则同 id 被误判重复。
        await Assert.That(protocol.PendingCount).IsEqualTo(0);
        await Assert.That(protocol.TryBegin("dsh-pick-1")).IsTrue();
    }

    [Test]
    public async Task ResolveScript_WithPath_JsonEscapesBackslashes()
    {
        string script = DesktopBridgeShim.BuildResolveScript("dsh-pick-1", @"C:\Users\foo");

        await Assert.That(script).IsEqualTo(
            """window.__dshDesktopBridgeResolve("dsh-pick-1","C:\\Users\\foo")""");
    }

    [Test]
    public async Task ResolveScript_Cancel_ResolvesNull()
    {
        string script = DesktopBridgeShim.BuildResolveScript("dsh-pick-1", null);

        await Assert.That(script).IsEqualTo(
            """window.__dshDesktopBridgeResolve("dsh-pick-1",null)""");
    }

    [Test]
    public async Task RejectScript_EscapesMessage()
    {
        string script = DesktopBridgeShim.BuildRejectScript("dsh-pick-1", """bad "path" """);

        // System.Text.Json 默认编码器把引号转义为 （合法 JSON/JS 字符串转义）。
        await Assert.That(script).IsEqualTo(
            """window.__dshDesktopBridgeReject("dsh-pick-1","bad \u0022path\u0022 ")""");
    }

    [Test]
    public async Task InstallScript_DefinesContractBridgeAndCallbacks()
    {
        // 契约面（ADR-0006）：官方插件找 window.dshDesktopDirectoryPicker.pick；
        // 页→宿主走 invokeCSharpAction；宿主→页回调走两个全局函数。
        await Assert.That(DesktopBridgeShim.InstallScript.Contains("window.dshDesktopDirectoryPicker")).IsTrue();
        await Assert.That(DesktopBridgeShim.InstallScript.Contains("invokeCSharpAction")).IsTrue();
        await Assert.That(DesktopBridgeShim.InstallScript.Contains("__dshDesktopBridgeResolve")).IsTrue();
        await Assert.That(DesktopBridgeShim.InstallScript.Contains("__dshDesktopBridgeReject")).IsTrue();
    }
}
