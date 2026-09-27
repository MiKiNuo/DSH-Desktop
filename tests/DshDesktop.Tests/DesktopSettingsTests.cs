using DshDesktop.App.Composition;
using DshDesktop.Infrastructure.Config;
using DshDesktop.Presentation.Avalonia.Features.Settings;

namespace DshDesktop.Tests;

public sealed class DesktopSettingsTests
{
    [Test]
    public async Task SafeMode_PublishesOnlyAfterPersistenceCompletes()
    {
        DshDesktopConfig config = new();
        TaskCompletionSource saved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        List<bool> published = [];
        bool savedValue = false;
        ConfigPersistence persistence = new((value, _) =>
        {
            savedValue = value.SafeMode;
            return saved.Task;
        });
        DesktopSettings settings = new(() => config, persistence, published.Add);

        Task operation = settings.SetSafeModeCoreAsync(true, CancellationToken.None);
        await Assert.That(savedValue).IsTrue();
        await Assert.That(published.Count).IsEqualTo(0);
        saved.SetResult();
        await operation;
        await Assert.That(published.Count).IsEqualTo(1);
        await Assert.That(published[0]).IsTrue();
    }

    [Test]
    public async Task SafeMode_SaveFailureKeepsMutationWithoutPublishing()
    {
        DshDesktopConfig config = new();
        int published = 0;
        ConfigPersistence persistence = new((_, _) => throw new IOException("save failed"));
        DesktopSettings settings = new(() => config, persistence, _ => published++);

        await Assert.That(async () => await settings.SetSafeModeCoreAsync(true, CancellationToken.None))
            .Throws<IOException>();
        await Assert.That(config.SafeMode).IsTrue();
        await Assert.That(published).IsEqualTo(0);
    }

    [Test]
    public async Task Settings_ReadsCurrentConfigurationAndActiveRuntimePath()
    {
        string root = Path.Combine(Path.GetTempPath(), "dsh-settings-projection");
        DshDesktopConfig current = new() { DshHome = Path.Combine(root, "dsh-home") };
        DesktopSettings settings = new(() => current, new ConfigPersistence((_, _) => Task.CompletedTask), _ => { });
        SettingsInfo borrowed = await settings.HandleGetSettingsInfo(new GetSettingsInfoRequest(), CancellationToken.None);
        current = new DshDesktopConfig
        {
            DshHome = Path.Combine(root, "new", "dsh-home"),
            ActiveDshRuntime = "1.2.3",
            SafeMode = true,
        };
        SettingsInfo owned = await settings.HandleGetSettingsInfo(new GetSettingsInfoRequest(), CancellationToken.None);

        await Assert.That(borrowed.DshRuntimeDirectory).IsEqualTo(Path.Combine(root, "runtime", "dsh"));
        await Assert.That(owned.DshRuntimeDirectory).IsEqualTo(Path.Combine(root, "new", "runtime", "dsh", "1.2.3"));
        await Assert.That(owned.SafeMode).IsTrue();
    }

    [Test]
    public async Task FlagMutation_UsesProvidedPersistenceAndCancellationToken()
    {
        DshDesktopConfig config = new();
        using CancellationTokenSource cancellation = new();
        DshDesktopConfig? saved = null;
        CancellationToken savedToken = default;
        DesktopSettings settings = new(() => config, new ConfigPersistence((value, token) =>
        {
            saved = value;
            savedToken = token;
            return Task.CompletedTask;
        }), _ => { });

        await settings.SetConfigFlagAsync(new SetDshChannelRequest("alpha"), cancellation.Token,
            value => value.DshChannel = "alpha", "Settings.DshChannel {Channel}", "alpha");
        await Assert.That(ReferenceEquals(saved, config)).IsTrue();
        await Assert.That(config.DshChannel).IsEqualTo("alpha");
        await Assert.That(savedToken).IsEqualTo(cancellation.Token);
    }

    [Test]
    public async Task UninitializedConfigurationAndUnsupportedPlatformPreserveErrors()
    {
        DesktopSettings settings = new(
            () => throw new InvalidOperationException("Runtime 编排尚未初始化完成，请稍候再试。"),
            new ConfigPersistence((_, _) => Task.CompletedTask), _ => { });
        await Assert.That(async () => await settings.HandleGetSettingsInfo(new GetSettingsInfoRequest(), CancellationToken.None))
            .Throws<InvalidOperationException>();
        await Assert.That(async () => await settings.HandleOpenPath(new OpenPathRequest("unused"), CancellationToken.None))
            .Throws<InvalidOperationException>();
    }
}
