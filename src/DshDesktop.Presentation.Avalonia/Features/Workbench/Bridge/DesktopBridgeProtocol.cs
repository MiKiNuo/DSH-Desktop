using System.Text.Json;
using System.Text.Json.Serialization;

namespace DshDesktop.Presentation.Avalonia.Features.Workbench.Bridge;

/// <summary>
/// 表示页内 shim 经 invokeCSharpAction 发来的桥请求消息（ADR-0006）。
/// </summary>
public sealed record DesktopBridgeRequest
{
    /// <summary>请求 id（shim 侧序号生成，宿主按 id 配对回调）。</summary>
    public string? Id { get; init; }

    /// <summary>请求类型，当前仅 "pick"（桥面只一个方法，不为想象需求扩展）。</summary>
    public string? Type { get; init; }
}

/// <summary>
/// 桥消息 JSON 序列化上下文（AOT 源生成，禁止反射序列化）。
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DesktopBridgeRequest))]
[JsonSerializable(typeof(string))]
public sealed partial class DesktopBridgeJsonContext : JsonSerializerContext;

/// <summary>
/// 表示 Desktop Bridge 消息协议（纯逻辑，可单测）：请求解析 + 挂起请求表（按 id 配对）。
/// 导航/刷新后 shim 序号归零，View 必须在重装 shim 时 <see cref="Reset"/>。
/// </summary>
public sealed class DesktopBridgeProtocol
{
    /// <summary>目录选择请求类型名。</summary>
    public const string PickType = "pick";

    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);

    /// <summary>当前挂起请求数。</summary>
    public int PendingCount => _pending.Count;

    /// <summary>
    /// 解析页内消息体；仅接受合法 pick 请求（未知类型/坏 JSON/缺 id 一律拒绝）。
    /// </summary>
    /// <param name="body">WebMessageReceived 的 Body。</param>
    /// <param name="id">解析出的请求 id。</param>
    /// <returns>是否为合法 pick 请求。</returns>
    public bool TryParseRequest(string? body, out string id)
    {
        id = string.Empty;
        if (string.IsNullOrEmpty(body))
        {
            return false;
        }

        DesktopBridgeRequest? request;
        try
        {
            request = JsonSerializer.Deserialize(body, DesktopBridgeJsonContext.Default.DesktopBridgeRequest);
        }
        catch (JsonException)
        {
            return false;
        }

        if (request is null
            || !string.Equals(request.Type, PickType, StringComparison.Ordinal)
            || string.IsNullOrEmpty(request.Id))
        {
            return false;
        }

        id = request.Id;
        return true;
    }

    /// <summary>
    /// 登记挂起请求；同 id 已挂起（shim 重装前旧请求未回，或异常重复）时拒绝。
    /// </summary>
    public bool TryBegin(string id) => _pending.Add(id);

    /// <summary>完成请求（回调已发出）；未知 id 为空操作。</summary>
    public void Complete(string id) => _pending.Remove(id);

    /// <summary>清空挂起表（导航完成重装 shim 时调用：页面上下文已重建，旧挂起无意义）。</summary>
    public void Reset() => _pending.Clear();
}
