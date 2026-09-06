using MiKiNuo.Mvi.Domain.MVI.Mediator;

namespace DshDesktop.Presentation.Avalonia.Features.Diagnostics;

/// <summary>
/// 表示运行诊断的跨层请求（§28 Mediator；组合根编排 DiagnosisRunner，结果发布到诊断事件流）。
/// </summary>
public sealed record RunDiagnosisRequest : IMviRequest<bool>;

/// <summary>
/// 表示导出诊断包的跨层请求（组合根打包 data/logs 为 zip）。
/// </summary>
/// <param name="DestinationPath">目标 zip 绝对路径。</param>
public sealed record ExportDiagnosticsBundleRequest(string DestinationPath) : IMviRequest<bool>;

/// <summary>
/// 表示打开日志目录的跨层请求（复用组合根 IPathOpener 端口；路径由组合根推导，Presentation 不感知 DataRoot）。
/// </summary>
public sealed record OpenLogsDirectoryRequest : IMviRequest<bool>;
