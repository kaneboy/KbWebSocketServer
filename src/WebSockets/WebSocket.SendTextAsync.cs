using System.Threading;
using System;
using System.Net.WebSockets;
using System.Buffers;
using System.Text;
using System.Threading.Tasks;

namespace KbWebSocketServer.WebSockets;

/// <summary>
/// 为<see cref="System.Net.WebSockets.WebSocket"/>添加扩展方法：SendTextAsync()。
/// </summary>
public static class WebSocketSendTextAsyncExtension
{
    /// <summary>
    /// 发送一条完整的文本消息。
    /// </summary>
    public static ValueTask SendTextAsync(
        this WebSocket webSocket,
        string text,
        CancellationToken cancelToken = default)
    {
        return SendTextAsync(webSocket, text.AsMemory(), cancelToken);
    }

    /// <summary>
    /// 发送一条完整的文本消息。
    /// </summary>
    public static async ValueTask SendTextAsync(
        this WebSocket webSocket,
        ReadOnlyMemory<char> text, 
        CancellationToken cancelToken = default)
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