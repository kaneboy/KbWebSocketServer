using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace KbWebSocketServer.ObjectModels;

/// <summary>
/// 客户端连接到WebSocket服务器的请求。
/// </summary>
public readonly struct WebSocketUpgradeRequest
{
    /// <summary>
    /// 客户端的Tcp连接。
    /// </summary>
    public TcpClient TcpClient { get; init; }

    /// <summary>
    /// 客户端的网络Stream。
    /// </summary>
    public Stream ClientStream { get; init; }

    /// <summary>
    /// 客户端请求的原始文本。
    /// </summary>
    public string RawText { get; init; }

    /// <summary>
    /// 客户端请求的头信息。
    /// </summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; }

    /// <summary>
    /// 返回指定字段的请求头信息。大小写不敏感。如果头信息未包含指定字段，返回null。
    /// </summary>
    public string? Get(string headerField)
    {
        if (Headers.TryGetValue(headerField, out var result))
            return result;

        var keys = Headers.Keys;
        foreach (string key in keys)
        {
            if (key.Equals(headerField, StringComparison.OrdinalIgnoreCase) &&
                Headers.TryGetValue(key, out result))
            {
                return result;
            }
        }

        return null;
    }

    /// <summary>
    /// 客户端的远端Ip地址。
    /// </summary>
    public IPAddress Ip
    {
        get
        {
            EndPoint? remoteEndPoint = TcpClient.Client.RemoteEndPoint;
            if (remoteEndPoint is IPEndPoint ipEndPoint)
            {
                return ipEndPoint.Address;
            }
            return IPAddress.None;
        }
    }
}
