using System;
using System.Net;
using System.Net.WebSockets;
using System.Threading.Tasks;

namespace KbWebSocketServer.ObjectModels;

/// <summary>
/// WebSocket握手上下文。
/// </summary>
public sealed class WebSocketUpgradeContext
{
    private readonly Func<WebSocketUpgradeContext, object?, ValueTask<WebSocket>> _accepter;
    private readonly object? _accepterState;
    private readonly Func<WebSocketUpgradeContext, object?, ValueTask> _rejecter;
    private readonly object? _rejecterState;

    /// <summary>
    /// 构造函数。
    /// </summary>
    internal WebSocketUpgradeContext(
        WebSocketUpgradeRequest request,
        WebSocketUpgradeResponse response,
        Func<WebSocketUpgradeContext, object?, ValueTask<WebSocket>> accepter,
        object? accepterState,
        Func<WebSocketUpgradeContext, object?, ValueTask> rejecter,
        object? rejecterState)
    {
        Request = request;
        Response = response;
        _accepter = accepter;
        _accepterState = accepterState;
        _rejecter = rejecter;
        _rejecterState = rejecterState;
    }

    /// <summary>
    /// 客户端连接请求。
    /// </summary>
    public WebSocketUpgradeRequest Request { get; }

    /// <summary>
    /// 客户端连接响应。
    /// </summary>
    public WebSocketUpgradeResponse Response { get; }

    /// <summary>
    /// 接受客户端的握手请求，返回与客户端建立的WebSocket连接。
    /// </summary>
    public ValueTask<WebSocket> AcceptWebSocketAsync()
    {
        Response.StatusCode = HttpStatusCode.SwitchingProtocols;
        return _accepter(this, _accepterState);
    }

    /// <summary>
    /// 拒绝客户端的握手请求。
    /// </summary>
    public ValueTask RejectWebSocketAsync(HttpStatusCode statusCode = HttpStatusCode.Unauthorized)
    {
        Response.StatusCode = statusCode;
        return _rejecter(this, _rejecterState);
    }
}
