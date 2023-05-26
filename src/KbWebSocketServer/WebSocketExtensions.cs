﻿using System;
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
        return ReceiveMessagesAsync(webSocket, MemoryPool<byte>.Shared, cancellationToken);
    }

    /// <summary>
    /// 接收消息。连接中断将使异步迭代器正常结束(而不会抛出任何异常)。
    /// </summary>
    public static IAsyncEnumerable<WebSocketMessage> ReceiveMessagesAsync(
        this WebSocket webSocket,
        MemoryPool<byte> memoryPool,
        CancellationToken cancellationToken = default)
    {
        return WebSocketReceiveMessagesAsyncExtension.ReceiveMessagesAsync(webSocket, memoryPool, cancellationToken);
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
        // corelib内置的ManagedWebSocket的SendAsync()实现，会在内部使用 ArrayPool<byte>.Shared 分配一个 byte[] 用作发送缓冲区。
        // 如果要发送的数据太大，会分配一个非常大的 byte[] 。
        //
        // 参考：https://source.dot.net/#System.Net.WebSockets/System/Net/WebSockets/ManagedWebSocket.cs,506ccd32c1633978
        //
        // 为了优化此逻辑，对于太大的数量，改成分为多次调用SendAsync()。

        return bytes.Length <= 65536 
            ? webSocket.SendAsync(bytes, WebSocketMessageType.Binary, true, cancelToken) 
            : SendBinaryBatchlyAsync(webSocket, bytes);
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
            await SendBinaryAsync(webSocket, buffer.AsMemory(0, bufferSize), cancelToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// 将一条完整二进制消息，分成多次批量发送。
    /// </summary>
    public static async ValueTask SendBinaryBatchlyAsync(this WebSocket webSocket, ReadOnlyMemory<byte> bytes)
    {
        const int batchMaxSize = 65536 - 14;
        int offset = 0;

        while (offset < bytes.Length)
        {
            int batchSize = Math.Min(batchMaxSize, bytes.Length - offset);
            ReadOnlyMemory<byte> batch = bytes.Slice(offset, batchSize);

            offset += batchSize;
            bool isLast = offset >= bytes.Length;

            await webSocket.SendAsync(batch, WebSocketMessageType.Binary, isLast, CancellationToken.None).ConfigureAwait(false);
        }
    }
}
