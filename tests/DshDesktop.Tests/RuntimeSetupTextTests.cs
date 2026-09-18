using DshDesktop.Presentation.Avalonia.Features.AppShell;

namespace DshDesktop.Tests;

/// <summary>
/// RuntimeSetupText 文案守卫：首启「缺少 DSH Runtime」弹窗的全部用户可见文案集中在此，
/// 防散写漂移（与 ConfirmDialogTextTests 同一约定）。
/// </summary>
public sealed class RuntimeSetupTextTests
{
    [Test]
    public async Task PromptText_TellsUserWhatIsMissingAndWhatHappens()
    {
        await Assert.That(RuntimeSetupText.Title).Contains("DSH Runtime");
        await Assert.That(RuntimeSetupText.Body).Contains("DSH Runtime");
        // 必须说明动作后果（下载）与前提（网络），用户点按钮前可见。
        await Assert.That(RuntimeSetupText.Body).Contains("下载");
        await Assert.That(RuntimeSetupText.Body).Contains("网络");
        // 按钮用具体动词，不用「确定」（ConfirmDialogText 同款约定）。
        await Assert.That(RuntimeSetupText.AcceptLabel).Contains("下载");
        await Assert.That(RuntimeSetupText.AcceptLabel).IsNotEqualTo("确定");
    }

    [Test]
    public async Task ProgressStages_AreDistinctAndInformative()
    {
        await Assert.That(RuntimeSetupText.DownloadingNodeStage).Contains("Node.js");
        await Assert.That(RuntimeSetupText.InstallingRuntimeStage("1.2.3")).Contains("1.2.3");
        await Assert.That(RuntimeSetupText.ResolvingVersionStage)
            .IsNotEqualTo(RuntimeSetupText.DownloadingNodeStage);
    }

    [Test]
    public async Task FailureText_KeepsOriginalReasonAndNextStep()
    {
        string body = RuntimeSetupText.ErrorBody("网络不可达");
        await Assert.That(body).Contains("网络不可达");
        await Assert.That(body).Contains("重试");
        await Assert.That(RuntimeSetupText.ErrorTitle).Contains("失败");
    }

    [Test]
    public async Task Toasts_DeclineSaysWhenAskedAgain_SuccessSaysStarting()
    {
        await Assert.That(RuntimeSetupText.DeclinedToast).Contains("下次启动");
        await Assert.That(RuntimeSetupText.CompletedToast).Contains("启动");
    }
}
