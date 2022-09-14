using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading.Tasks;

namespace KbWebSocketServer.ObjectModels;

/// <summary>
/// WebSocket握手上下文。
/// </summary>
public sealed class WebSocketAcceptContext
{
    private readonly Func<WebSocketAcceptContext, object?, ValueTask<WebSocket>> _wsBuilder;
    private readonly object? _wsBuilderState;

    /// <summary>
    /// 构造函数。
    /// </summary>
    internal WebSocketAcceptContext(
        TcpClient tcpClient,
        Stream clientStream,
        string requestRawText,
        IDictionary<string, string> requestHeaders,
        Func<WebSocketAcceptContext, object?, ValueTask<WebSocket>> wsBuilder,
        object? wsBuilderState)
    {
        TcpClient = tcpClient;
        ClientStream = clientStream;
        RequestRawText = requestRawText;
        RequestHeaders = requestHeaders;
        _wsBuilder = wsBuilder;
        _wsBuilderState = wsBuilderState;
    }

    /// <summary>
    /// 客户端的Tcp连接。
    /// </summary>
    public TcpClient TcpClient { get; }

    /// <summary>
    /// 客户端连接的网络Stream。
    /// </summary>
    /// <remarks>
    /// 如果需要服务器对网络Stream进行封装（比如使用SslStream或GZipStream裹住原始网络Stream，为数据传输添加Ssl或压缩功能），可以重新设置此属性的值。
    /// </remarks>
    public Stream ClientStream { get; set; }

    /// <summary>
    /// 客户端请求的原始文本。
    /// </summary>
    public string RequestRawText { get; }

    /// <summary>
    /// 客户端请求的头信息。
    /// </summary>
    public IDictionary<string, string> RequestHeaders { get; }

    /// <summary>
    /// 返回给客户端的响应代码。
    /// </summary>
    public HttpStatusCode ResponseStatusCode { get; set; } = HttpStatusCode.Unauthorized;

    /// <summary>
    /// 返回给客户端的头信息。
    /// </summary>
    public IDictionary<string, string> ResponseHeaders { get; } = new Dictionary<string, string>();

    /// <summary>
    /// 接受客户端的握手请求，返回与客户端建立的WebSocket连接。
    /// </summary>
    public ValueTask<WebSocket> AcceptWebSocketAsync()
    {
        ResponseStatusCode = HttpStatusCode.SwitchingProtocols;
        return _wsBuilder(this, _wsBuilderState);
    }
}
