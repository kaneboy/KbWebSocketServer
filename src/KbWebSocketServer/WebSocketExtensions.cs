using System;
using System.Buffers;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KbWebSocketServer.WebSockets;

namespace KbWebSocketServer;

/// <summary>
/// 为<see cref="WebSocket"/>添加的扩展方法。
/// </summary>
public static class WebSocketExtensions
{
    /// <summary>
    /// 接收消息。连接中断将使异步迭代器正常结束(而不会抛出任何异常)。
    /// </summary>
    public static IAsyncEnumerable<WebSocketMessage> ReceiveMessagesAsync(
        this WebSocket webSocket,
        CancellationToken cancellationToken = default)
    {
        return WebSocketReceiveMessagesAsyncExtension.ReceiveMessagesAsync(webSocket, cancellationToken);
    }

    /// <summary>
    /// 发送一条完整的二进制消息。
    /// </summary>
    public static ValueTask SendBinaryAsync(this WebSocket webSocket, byte[] bytes, int start, int length, CancellationToken cancelToken = default)
    {
        return SendBinaryAsync(webSocket, bytes.AsMemory(start, length), cancelToken);
    }

    /// <summary>
    /// 发送一条完整的二进制消息。
    /// </summary>
    public static ValueTask SendBinaryAsync(this WebSocket webSocket, ReadOnlyMemory<byte> bytes, CancellationToken cancelToken = default)
    {
        return webSocket.SendAsync(bytes, WebSocketMessageType.Binary, true, cancelToken);
    }

    /// <summary>
    /// 发送一条完整的文本消息。
    /// </summary>
    public static ValueTask SendTextAsync(this WebSocket webSocket, string text, CancellationToken cancelToken = default)
    {
        return SendTextAsync(webSocket, text.AsMemory(), cancelToken);
    }

    /// <summary>
    /// 发送一条完整的文本消息。
    /// </summary>
    public static async ValueTask SendTextAsync(this WebSocket webSocket, ReadOnlyMemory<char> text, CancellationToken cancelToken = default)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetByteCount(text.Span));
        int bufferSize = Encoding.UTF8.GetBytes(text.Span, buffer);

        try
        {
            await webSocket.SendAsync(
                buffer.AsMemory(0, bufferSize),
                WebSocketMessageType.Text,
                true,
                cancelToken
            ).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
