using System.Collections.Generic;
using System.Net;

namespace KbWebSocketServer.ObjectModels;

/// <summary>
/// 客户端连接到WebSocket服务器的响应。
/// </summary>
public sealed class WebSocketUpgradeResponse
{
    /// <summary>
    /// 返回给客户端的响应代码。
    /// </summary>
    internal HttpStatusCode StatusCode { get; set; } = HttpStatusCode.SwitchingProtocols;

    /// <summary>
    /// 返回给客户端的头信息。
    /// </summary>
    public IDictionary<string, string> Headers { get; } = new Dictionary<string, string>();

    /// <summary>
    /// 设置返回给客户端的头信息。
    /// </summary>
    public void Set(string headerField, string headerValue)
    {
        Headers[headerField] = headerValue;
    }
}
