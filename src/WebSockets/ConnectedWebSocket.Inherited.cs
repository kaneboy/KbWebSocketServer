using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace KbWebSocketServer.WebSockets;

sealed partial class ConnectedWebSocket {

    /// <inheritdoc />
    public override void Abort() {
        _webSocket.Abort();
    }

    /// <inheritdoc />
    public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) {
        return _webSocket.CloseAsync(closeStatus, statusDescription, cancellationToken);
    }

    /// <inheritdoc />
    public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) {
        return _webSocket.CloseOutputAsync(closeStatus, statusDescription, cancellationToken);
    }

    /// <inheritdoc />
    public override void Dispose() {
        Dispose(true);
    }

    /// <summary>
    /// 不支持直接调用此方法，将抛出<see cref="NotSupportedException"/>异常。使用<see cref="TextMessageReceived"/>和<see cref="BinaryMessageReceived"/>事件获取收到的消息。
    /// </summary>
    public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<Byte> buffer, CancellationToken cancellationToken) {
        throw new NotSupportedException("不支持直接调用ReceiveAsync()接收消息。");
    }

    /// <inheritdoc />
    public override Task SendAsync(ArraySegment<Byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) {
        return _webSocket.SendAsync(buffer, messageType, endOfMessage, cancellationToken);
    }

    /// <inheritdoc />
    public override WebSocketCloseStatus? CloseStatus => _webSocket.CloseStatus;

    /// <inheritdoc />
    public override string? CloseStatusDescription => _webSocket.CloseStatusDescription;

    /// <inheritdoc />
    public override WebSocketState State => _webSocket.State;

    /// <inheritdoc />
    public override string? SubProtocol => _webSocket.SubProtocol;

}