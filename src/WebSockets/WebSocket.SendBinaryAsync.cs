using System.Threading;
using System;
using System.Net.WebSockets;
using System.Threading.Tasks;

namespace KbWebSocketServer.WebSockets;

/// <summary>
/// 为<see cref="System.Net.WebSockets.WebSocket"/>添加扩展方法：SendBinaryAsync()。
/// </summary>
public static class WebSocketSendBinaryAsyncExtension
{
    /// <summary>
    /// 向客户端发送二进制消息。
    /// </summary>
    public static ValueTask SendBinaryAsync(this WebSocket webSocket, byte[] bytes, int start, int length, CancellationToken cancelToken = default)
    {
        return SendBinaryAsync(webSocket, bytes.AsMemory(start, length), cancelToken);
    }

    /// <summary>
    /// 向客户端发送二进制消息。
    /// </summary>
    public static ValueTask SendBinaryAsync(this WebSocket webSocket, ReadOnlyMemory<byte> bytes, CancellationToken cancelToken = default)
    {
        return webSocket.SendAsync(bytes, WebSocketMessageType.Binary, true, cancelToken);
    }
}