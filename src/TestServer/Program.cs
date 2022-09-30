using System.Net;
using System.Collections.Immutable;
using System.Net.WebSockets;
using KbWebSocketServer;
using System.Net.Security;
using System.IO.Compression;

namespace TestServer;

internal class Program
{
    static void Main(string[] args)
    {
        var wss = new WebSocketServer(8888);

        //wss.ClientStreamDecorator = stream =>
        //{
        //    return new SslStream(new GZipStream(stream, CompressionMode.Decompress));
        //};

        wss.Start(async ctx =>
        {
            var ws = await ctx.AcceptWebSocketAsync();
            Echo(ws);
        });

        Console.ReadLine();
    }

    static async ValueTask Echo(WebSocket ws)
    {
        await foreach (var message in ws.ReceiveMessagesAsync())
        {
            if (message.MessageType == WebSocketMessageType.Text)
            {
                string text = message.Text.ToString();
                Console.WriteLine(text);

                await ws.SendTextAsync($"Reply: {message.Text}");
            }
        }

        Console.WriteLine("Someone disconnected.");
    }
}
