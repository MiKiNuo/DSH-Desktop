using System.Net;
using System.Net.Sockets;

namespace DshDesktop.Infrastructure.Runtime;

/// <summary>
/// 表示空闲端口探测（ADR-0001：listen 端口 0 让 OS 分配再释放，与 Electron reservePort 同法）。
/// </summary>
public static class PortProbe
{
    /// <summary>
    /// 探测一个当前空闲的 TCP 端口。
    /// </summary>
    /// <param name="host">监听地址。</param>
    /// <returns>空闲端口号。</returns>
    public static int FindFreeTcpPort(string host)
    {
        TcpListener listener = new(IPAddress.Parse(host), 0);
        try
        {
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
