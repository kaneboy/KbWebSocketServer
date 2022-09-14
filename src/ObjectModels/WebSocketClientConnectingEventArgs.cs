using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace KbWebSocketServer.ObjectModels;

/// <summary>
/// 新客户端正在连接到WebSocket服务器事件参数。
/// </summary>
public sealed class WebSocketClientConnectingEventArgs : EventArgs
{
    /// <summary>
    /// 构造函数。
    /// </summary>
    public WebSocketClientConnectingEventArgs(
        long clientId,
        TcpClient tcpClient, 
        string rawText, 
        IDictionary<string, string> headers)
    {
        ClientId = clientId;
        TcpClient = tcpClient;
        RawText = rawText;
        Headers = headers;
    }

    /// <summary>
    /// 标识客户端连接的序列号。
    /// </summary>
    public long ClientId { get; }

    /// <summary>
    /// 客户端的Tcp连接。
    /// </summary>
    public TcpClient TcpClient { get; }

    /// <summary>
    /// HTTP请求的原始文本。
    /// </summary>
    public string RawText { get; }

    /// <summary>
    /// HTTP请求的头信息。
    /// </summary>
    public IDictionary<string, string> Headers { get; }

    /// <summary>
    /// 设置此属性的值，标识是否接受此连接请求。
    /// </summary>
    public ValueTask<bool> Accepted { get; set; }

    /// <summary>
    /// 需要返回给客户端的头信息。
    /// </summary>
    public IDictionary<string, string> ResponseHeaders { get; } = new Dictionary<string, string>();
}