[![NuGet](https://img.shields.io/nuget/v/KbWebSocketServer.svg?label=NuGet&logo=NuGet)](https://www.nuget.org/packages/KbWebSocketServer/)

# KbWebSocketServer

`KbWebSocketServer` is a lightweight .net websocket server library.
- Simple Api interface
- No 3rd-party package dependency required
- Target framework: net5.0;net6.0;net7.0

# Usage Tutorials

## Install package

```powershell
Install-Package KbWebSocketServer
```

Or, if you don't want to add another package to your project, you can just copy all source files into your codebase. `KbWebSocketServer` is a clean and lightweight project, it doesn't require any additional package but only .net coreclr library itself.

## Start up a websocket server and accept client requests

```c#
// specify server listening IP and port.
var wss = new WebSocketServer(8000);

// start up server, pass in a client request handler.
wss.Start(async ctx => 
{
    // accept this client request.
    var ws = await ctx.AcceptWebSocketAsync();
    Write(ws);
});

async ValueTask Write(WebSocket ws)
{
    await foreach (var message in ws.ReceiveMessagesAsync())
    {
        if (message.MessageType == WebSocketMessageType.Text)
        {
            Console.WriteLine(message.Text.ToString());
        }
    }
}
```

## Reject client requests

```c#
var wss = new WebSocketServer(8000);

wss.Start(async ctx => 
{
    // set an error status code.
    ctx.ResponseStatusCode = HttpStatusCode.Unauthorized;
    // put some error information in response headers,
    // to allow client get error details from server response.
    //
    // it's optional, but useful.
    ctx.ResponseHeaders.Add("x-custom-error", "You shall not pass!");
});
```

## Receive/send messages from/to a client

```c#
var wss = new WebSocketServer(8000);

wss.Start(async ctx => 
{
    var ws = await ctx.AcceptWebSocketAsync();
    Echo(ws);
});

async ValueTask Echo(WebSocket ws)
{
    // ReceiveMessagesAsync() returns an IAsyncEnumerable,
    // which can be consumed by `await foreach`.
    await foreach (var message in ws.ReceiveMessagesAsync())
    {
        // message type can be `Text` or `Binary`.
        if (message.MessageType == WebSocketMessageType.Text)
        {
            // send back a text message.
            await ws.SendTextAsync($"Reply: {message.Text}");
        }
    }
}
```

## Broadcast to all clients

```c#
var wss = new WebSocketServer(8000);

var clients = ImmutableArray<WebSocket>.Empty;

wss.Start(async ctx =>
{
    var ws = await ctx.AcceptWebSocketAsync();
    // put new client into a collection.
    ImmutableInterlocked.Update(
        ref clients,
        arr => arr.Add(ws));
    Pub(ws);
});

async ValueTask Pub(WebSocket ws)
{
    var textMessages = ws
        .ReceiveMessagesAsync()
        .Where(msg => msg.MessageType == WebSocketMessageType.Text);
    
    await foreach (var message in textMessages)
    {
        // broadcast message to all opened clients.
        foreach (var client in clients)
        {
            if (client.State == WebSocketState.Open)
            {
                await client.SendTextAsync($"Pub: {message.Text}");
            }
        }
    }
}
```

## Determine client disconnects

```c#
var wss = new WebSocketServer(8000);

wss.Start(async ctx => 
{
    var ws = await ctx.AcceptWebSocketAsync();
    Echo(ws);
});

async ValueTask Echo(WebSocket ws)
{
    var textMessages = ws
        .ReceiveMessagesAsync()
        .Where(msg => msg.MessageType == WebSocketMessageType.Text);

    // when this client is disconnected,
    // it'll break the `await foreach` loop.
    await foreach (var message in textMessages)
    {
        // ...
    }

    if (ws.State != WebSocketState.Open)
    {
        Console.WriteLine($"a client is disconnected. state={ws.State}");
    }
}
```

## Close client connection from server

```c#
var wss = new WebSocketServer(8000);

wss.Start(async ctx => 
{
    var ws = await ctx.AcceptWebSocketAsync();
    Echo(ws);
});

async ValueTask Echo(WebSocket ws)
{
    var textMessages = ws
        .ReceiveMessagesAsync()
        .Where(msg => msg.MessageType == WebSocketMessageType.Text);

    await foreach (var message in textMessages)
    {
        if (message.Text.ToString().Equals("PleaseClose", StringComparison.OrdinalIgnoreCase))
        {
            // close client connection.
            await ws.CloseAsync(
                WebSocketCloseStatus.NormalClosure, 
                "Closed by server.", 
                CancellationToken.None);
            break;
        }
    }

    if (ws.State != WebSocketState.Open)
    {
        Console.WriteLine($"a client is disconnected. state={ws.State}");
    }
}
```