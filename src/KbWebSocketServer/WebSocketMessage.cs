using System;
using System.Buffers;
using System.Net.WebSockets;

namespace KbWebSocketServer;

/// <summary>
/// 一条WebSocket消息。消息类型可能是文本或二进制。
/// </summary>
public readonly struct WebSocketMessage
{
    /// <summary>
    /// 构造一个二进制类型消息。
    /// </summary>
    internal WebSocketMessage(ReadOnlySequence<byte> bytes)
    {
        MessageType = WebSocketMessageType.Binary;
        Binary = bytes;
        Text = ReadOnlyMemory<char>.Empty;
    }

    /// <summary>
    /// 构造一个文本类型消息。
    /// </summary>
    internal WebSocketMessage(ReadOnlyMemory<char> chars)
    {
        MessageType = WebSocketMessageType.Text;
        Binary = ReadOnlySequence<byte>.Empty;
        Text = chars;
    }

    /// <summary>
    /// 消息类型。二进制 or 文本。
    /// </summary>
    public WebSocketMessageType MessageType { get; }

    /// <summary>
    /// 消息包含的二进制内容（如果消息类型是二进制）。
    /// </summary>
    /// <remarks>
    /// 支持此属性值的内存缓冲区将被自动回收重用，请勿将此属性的使用范围扩展到其他函数栈帧。
    /// 如果确实需要，将此属性值复制一份使用。
    /// </remarks>
    public ReadOnlySequence<byte> Binary { get; }

    /// <summary>
    /// 消息包含的文本内容（如果消息类型是文本）。
    /// </summary>
    /// <remarks>
    /// 支持此属性值的内存缓冲区将被自动回收重用，请勿将此属性的使用范围扩展到其他函数栈帧。
    /// 如果确实需要，将此属性值复制一份使用。
    /// </remarks>
    public ReadOnlyMemory<char> Text { get; }
}
