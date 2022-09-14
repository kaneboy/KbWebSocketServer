using System.Net.WebSockets;

namespace KbWebSocketServer.ObjectModels;

/// <summary>
/// 新客户端连接到WebSocket服务器事件参数。
/// </summary>
public readonly struct WebSocketClientConnectedEventArgs {

    /// <summary>
    /// 标识客户端连接的序列号。
    /// </summary>
    public long ClientId { get; init; }

    /// <summary>
    /// 与客户端的WebSocket连接。
    /// </summary>
    public WebSocket Client { get; init; }

}