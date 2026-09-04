using DshDesktop.Domain.Plugins;
using MiKiNuo.Mvi.Application.MVI.Reducer;
using MiKiNuo.Mvi.Domain.DI;
using MiKiNuo.Mvi.Domain.MVI.Reducer;

namespace DshDesktop.Presentation.Avalonia.Features.Plugins;

/// <summary>
/// 表示 Plugins 规约器。纯函数，禁止 IO（§9）。
/// </summary>
[MviFeature]
public sealed partial class PluginsReducer
    : MviReducerBase<PluginsState, PluginsIntent, PluginsEffect>
{
    /// <summary>
    /// 处理加载插件清单意图。
    /// </summary>
    [MviReduce(typeof(PluginsIntent.LoadPlugins))]
    private MviReduceResult<PluginsState, PluginsEffect> HandleLoadPlugins(
        PluginsState state,
        PluginsIntent.LoadPlugins intent)
    {
        return WithEffect(
            state with { PendingOperation = "加载插件清单…", LastError = null },
            new PluginsEffect.LoadPlugins());
    }

    /// <summary>
    /// 处理启用插件意图。
    /// </summary>
    [MviReduce(typeof(PluginsIntent.EnablePlugin))]
    private MviReduceResult<PluginsState, PluginsEffect> HandleEnablePlugin(
        PluginsState state,
        PluginsIntent.EnablePlugin intent)
    {
        return WithEffect(
            state with { PendingOperation = $"启用 {intent.Name}…", LastError = null },
            new PluginsEffect.SetPluginEnabled(intent.Name, true));
    }

    /// <summary>
    /// 处理禁用插件意图。
    /// </summary>
    [MviReduce(typeof(PluginsIntent.DisablePlugin))]
    private MviReduceResult<PluginsState, PluginsEffect> HandleDisablePlugin(
        PluginsState state,
        PluginsIntent.DisablePlugin intent)
    {
        return WithEffect(
            state with { PendingOperation = $"禁用 {intent.Name}…", LastError = null },
            new PluginsEffect.SetPluginEnabled(intent.Name, false));
    }

    /// <summary>
    /// 处理卸载插件意图。
    /// </summary>
    [MviReduce(typeof(PluginsIntent.UninstallPlugin))]
    private MviReduceResult<PluginsState, PluginsEffect> HandleUninstallPlugin(
        PluginsState state,
        PluginsIntent.UninstallPlugin intent)
    {
        return WithEffect(
            state with { PendingOperation = $"卸载 {intent.Name}…", LastError = null },
            new PluginsEffect.UninstallPlugin(intent.Name));
    }

    /// <summary>
    /// 处理插件清单已刷新回流意图。
    /// </summary>
    [MviReduce(typeof(PluginsIntent.PluginsLoaded))]
    private MviReduceResult<PluginsState, PluginsEffect> HandlePluginsLoaded(
        PluginsState state,
        PluginsIntent.PluginsLoaded intent)
    {
        return Unchanged(state with
        {
            Plugins = intent.Plugins,
            PendingOperation = null,
            Operation = null,
            LastError = null,
        });
    }

    /// <summary>
    /// 处理安装插件意图：声明事务副作用，阶段进度由 PluginOperationChanged 回流驱动。
    /// </summary>
    [MviReduce(typeof(PluginsIntent.InstallPlugin))]
    private MviReduceResult<PluginsState, PluginsEffect> HandleInstallPlugin(
        PluginsState state,
        PluginsIntent.InstallPlugin intent)
    {
        if (state.Operation is { Stage: not PluginOperationStage.Completed and not PluginOperationStage.Failed })
        {
            return Unchanged(state); // 已有事务进行中。
        }

        return WithEffect(
            state with { PendingOperation = $"安装 {intent.Source}…", LastError = null },
            new PluginsEffect.InstallPlugin(intent.Source));
    }

    /// <summary>
    /// 处理禁用全部第三方插件意图。
    /// </summary>
    [MviReduce(typeof(PluginsIntent.DisableAllThirdParty))]
    private MviReduceResult<PluginsState, PluginsEffect> HandleDisableAllThirdParty(
        PluginsState state,
        PluginsIntent.DisableAllThirdParty intent)
    {
        return WithEffect(
            state with { PendingOperation = "禁用全部第三方插件…", LastError = null },
            new PluginsEffect.DisableAllThirdParty());
    }

    /// <summary>
    /// 处理安装事务阶段推进回流意图。
    /// </summary>
    [MviReduce(typeof(PluginsIntent.PluginOperationChanged))]
    private MviReduceResult<PluginsState, PluginsEffect> HandlePluginOperationChanged(
        PluginsState state,
        PluginsIntent.PluginOperationChanged intent)
    {
        PluginOperationStage stage = intent.Operation.Stage;
        return Unchanged(state with
        {
            Operation = intent.Operation,
            PendingOperation = stage is PluginOperationStage.Completed or PluginOperationStage.Failed
                ? null
                : $"{stage}：{intent.Operation.PluginName ?? "…"}",
            LastError = intent.Operation.Error ?? state.LastError,
        });
    }

    /// <summary>
    /// 处理插件操作失败回流意图。
    /// </summary>
    [MviReduce(typeof(PluginsIntent.PluginOperationFailed))]
    private MviReduceResult<PluginsState, PluginsEffect> HandlePluginOperationFailed(
        PluginsState state,
        PluginsIntent.PluginOperationFailed intent)
    {
        return Unchanged(state with
        {
            PendingOperation = null,
            LastError = intent.Error,
        });
    }
}
