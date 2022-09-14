using System;
using System.Net.WebSockets;

namespace KbWebSocketServer.ObjectModels;

/// <summary>
/// 一条WebSocket消息。消息类型可能是文本或二进制。
/// </summary>
public readonly struct WebSocketMessage
{
    private readonly WebSocketMessageType _messageType;
    private readonly byte[]? _binaryBuffer;
    private readonly int _binaryBufferSize;
    private readonly char[]? _textBuffer;
    private readonly int _textBufferSize;

    /// <summary>
    /// 构造一个二进制类型消息。
    /// </summary>
    internal WebSocketMessage(byte[] binaryBuffer, int binaryBufferSize)
    {
        _messageType = WebSocketMessageType.Binary;
        _binaryBuffer = binaryBuffer;
        _binaryBufferSize = binaryBufferSize;
        _textBuffer = null;
        _textBufferSize = 0;
    }

    /// <summary>
    /// 构造一个文本类型消息。
    /// </summary>
    internal WebSocketMessage(char[] textBuffer, int textBufferSize)
    {
        _messageType = WebSocketMessageType.Text;
        _binaryBuffer = null;
        _binaryBufferSize = 0;
        _textBuffer = textBuffer;
        _textBufferSize = textBufferSize;
    }

    /// <summary>
    /// 消息类型。二进制 or 文本。
    /// </summary>
    public WebSocketMessageType MessageType => _messageType;

    /// <summary>
    /// 消息包含的二进制内容（如果消息类型是二进制）。
    /// </summary>
    /// <remarks>
    /// 支持此属性值的内存缓冲区将被自动回收重用，请勿将此属性的使用范围扩展到其他函数栈帧。
    /// 如果确实需要，将此属性值复制一份使用。
    /// </remarks>
    public ReadOnlySpan<byte> Binary => 
        _binaryBuffer != null
            ? _binaryBuffer.AsSpan(0, _binaryBufferSize)
            : ReadOnlySpan<byte>.Empty;

    /// <summary>
    /// 消息包含的文本内容（如果消息类型是文本）。
    /// </summary>
    /// <remarks>
    /// 支持此属性值的内存缓冲区将被自动回收重用，请勿将此属性的使用范围扩展到其他函数栈帧。
    /// 如果确实需要，将此属性值复制一份使用。
    /// </remarks>
    public ReadOnlySpan<char> Text =>
        _textBuffer != null
            ? _textBuffer.AsSpan(0, _textBufferSize)
            : ReadOnlySpan<char>.Empty;
}