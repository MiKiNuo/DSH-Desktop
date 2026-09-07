using System.Text.Json;

namespace DshDesktop.Presentation.Avalonia.Features.Workbench.Bridge;

/// <summary>
/// Desktop Bridge 页内 shim 文本与宿主回调脚本生成（ADR-0006：shim 文本为常量；
/// 回调脚本参数经 JSON 转义——JSON 字符串字面量即合法 JS 字符串字面量）。
/// </summary>
public static class DesktopBridgeShim
{
    /// <summary>
    /// 注入 shim：定义 window.dshDesktopDirectoryPicker.pick（官方插件契约）与两个宿主回调全局函数。
    /// 每次 NavigationCompleted 重装（SPA 导航/刷新后桥可能丢）。
    /// </summary>
    public const string InstallScript = """
        (function () {
          var pending = {};
          var seq = 0;
          window.__dshDesktopBridgeResolve = function (id, path) {
            var entry = pending[id];
            if (!entry) return;
            delete pending[id];
            entry.resolve(path);
          };
          window.__dshDesktopBridgeReject = function (id, message) {
            var entry = pending[id];
            if (!entry) return;
            delete pending[id];
            entry.reject(new Error(message));
          };
          window.dshDesktopDirectoryPicker = {
            pick: function () {
              return new Promise(function (resolve, reject) {
                var id = "dsh-pick-" + (++seq);
                pending[id] = { resolve: resolve, reject: reject };
                invokeCSharpAction(JSON.stringify({ id: id, type: "pick" }));
              });
            }
          };
        })();
        """;

    /// <summary>生成 resolve 回调脚本（path 为 null = 用户取消，契约 resolve(null)）。</summary>
    public static string BuildResolveScript(string id, string? path)
        => $"window.__dshDesktopBridgeResolve({Json(id)},{(path is null ? "null" : Json(path))})";

    /// <summary>生成 reject 回调脚本（异常 → 契约 reject(Error)）。</summary>
    public static string BuildRejectScript(string id, string message)
        => $"window.__dshDesktopBridgeReject({Json(id)},{Json(message)})";

    private static string Json(string value)
        => JsonSerializer.Serialize(value, DesktopBridgeJsonContext.Default.String);
}
