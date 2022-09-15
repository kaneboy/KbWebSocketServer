using System;
using System.IO;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace KbWebSocketServer.WebSockets;

/// <summary>
/// 与客户端的WebSocket连接。
/// </summary>
/// <remarks>
/// 目前这个类没有任何自定义功能，它使用组合模式将真正实现功能的WebSocket类简单做了一个封装。
/// 使用此类的目的，是为未来的扩展性提供可能。
/// </remarks>
internal sealed class ConnectedWebSocket : WebSocket
{

    private readonly WebSocket _webSocket;
    private readonly TcpClient _tcpClient;
    private readonly Stream _stream;

    /// <summary>
    /// 使用底层WebSocket对象初始化对象。
    /// </summary>
    internal ConnectedWebSocket(
        TcpClient tcpClient,
        Stream stream,
        WebSocket webSocket)
    {
        _tcpClient = tcpClient;
        _stream = stream;
        _webSocket = webSocket;
    }

    ~ConnectedWebSocket() => Dispose(false);

    /// <inheritdoc />
    public override void Abort()
    {
        _webSocket.Abort();
    }

    /// <inheritdoc />
    public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
    {
        return _webSocket.CloseAsync(closeStatus, statusDescription, cancellationToken);
    }

    /// <inheritdoc />
    public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
    {
        return _webSocket.CloseOutputAsync(closeStatus, statusDescription, cancellationToken);
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        Dispose(true);
    }

    /// <inheritdoc />
    public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
    {
        return _webSocket.ReceiveAsync(buffer, cancellationToken);
    }

    /// <inheritdoc />
    public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
    {
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

    private void Dispose(bool disposing)
    {
        if (disposing)
        {
            GC.SuppressFinalize(this);
        }
        _webSocket.Dispose();
        _stream.Dispose();
        _tcpClient.Dispose();
    }
}