using System.Collections.Immutable;
using System.Net.WebSockets;
using KbWebSocketServer;

namespace TestServer;

internal class Program
{
    static void Main(string[] args)
    {
        var wss = new WebSocketServer(8888);

        List<WebSocket> clients = new List<WebSocket>();

        wss.Start(async ctx =>
        {
            var ws = await ctx.AcceptWebSocketAsync();
            lock (clients)
                clients.Add(ws);
            Echo(clients, ws);
        });

        Console.ReadLine();
    }

    static async ValueTask Echo(List<WebSocket> clients, WebSocket ws)
    {
        await foreach (var message in ws.ReceiveMessagesAsync())
        {
            if (message.MessageType == WebSocketMessageType.Text)
            {
                string text = message.Text.ToString();
                Console.WriteLine(text);

                ImmutableArray<WebSocket> clientArr;
                lock (clients)
                {
                    clientArr = clients.ToImmutableArray();
                }

                foreach (WebSocket client in clientArr)
                {
                    if (client.State == WebSocketState.Open)
                    {
                        await client.SendTextAsync(text);
                    }
                }
            }
        }

        Console.WriteLine("Someone disconnected.");

        lock (clients)
            clients.Remove(ws);
    }
}
