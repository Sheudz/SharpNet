# SharpNet
SharpNet is a C# library for working with TCP network connections. It provides a straightforward way to create a TCP server, handle incoming messages, and send responses to clients. The library supports listening for messages with specific identifiers and processing these messages asynchronously.

# Installation
To install the SharpNet library, you need to use the .NET CLI to add it to your project. Use the following command to install the package:

```
dotnet add package SharpNet --version 1.0.1
```

This command will add the SharpNet package to your project, allowing you to use its classes and methods in your application.

# Examples

### Server (SharpNET)
```
using System.Net.Sockets;

namespace Test
{
    public class Program
    {
        private static SharpNet.SharpNet server;

        public static async Task Main(string[] args)
        {
            await StartServerAsync(5555);
        }

        public static async Task StartServerAsync(int port)
        {
            server = new SharpNet.SharpNet();
            server.separator = '|';
            server.StartServer(port);
            await HandleAsync();
        }

        public static async Task RestartServerAsync(int newPort)
        {
            if (server != null)
            {
                server.StopServer();
            }
            await StartServerAsync(newPort);
        }

        private static async Task HandleAsync()
        {
            server.Listen("TESTREQUEST1337", async (TcpClient client, string message) =>
            {
                await server.SendMessage(client, null, $"{message}+1337"); // If you don't need to transfer a packetId
                await server.SendMessage(client, "response1337", $"{message}+1337"); // If you need to transfer a packetId
            });

            // Prevent the application from exiting immediately
            await Task.Delay(-1); // Infinite delay
        }
    }
}

```
### Client (Net.Sockets)
```
using System;
using System.Net.Sockets;
using System.Text;

class Program
{
    static void Main()
    {
        string serverIp = "127.0.0.1";
        int serverPort = 5555;

        try
        {
            using (TcpClient client = new TcpClient(serverIp, serverPort))
            {
                NetworkStream stream = client.GetStream();

                string messageToSend = "TESTREQUEST1337|Hello";
                byte[] messageBytes = Encoding.ASCII.GetBytes(messageToSend);
                stream.Write(messageBytes, 0, messageBytes.Length);
                Console.WriteLine("sent message: " + messageToSend);

                byte[] responseBytes = new byte[1024];
                int bytesRead = stream.Read(responseBytes, 0, responseBytes.Length);
                string responseMessage = Encoding.ASCII.GetString(responseBytes, 0, bytesRead);
                Console.WriteLine("response from server: " + responseMessage);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine("Error: " + e.Message);
        }
    }
}
```
# Documentation

### Declare the SharpNet Server Instance
First, you need to declare a SharpNet server instance. This instance will be used to manage connections, listen for incoming messages, and send responses. For example:
```
public class Program
{
    private static SharpNet.SharpNet MyServer; // Declare the "MyServer" instance

    public static void Main(string[] args)
    {
        MyServer = new SharpNet.SharpNet(); // Initialize the "MyServer" instance
        // Your code here
    }
}

```
## Methods

### `StartServer`

- **Description:** Starts the TCP server on the specified port.
- **Arguments:**
  - `int port`: The port number on which the server should listen for incoming connections.
- **Returns:** `void`

**Example**:
```
server.StartServer(port);
```

### `StopServer`

- **Description:** Stops the TCP server and closes all active connections.
- **Arguments:** None
- **Returns:** `void`

**Example**:
```
server.Stop();
```

### `Listen`

- **Description:** Registers a callback to be invoked when a message with a specific packet identifier is received.
- **Arguments:**
  - `string packetId`: The identifier of the packet to listen for.
  - `Action<TcpClient, string> callback`: A callback method that will be invoked when a message with the specified packet identifier is received. The callback receives the message and the `TcpClient` object as arguments.
- **Returns:** `void`

**Example**:
```
server.Listen("TESTREQUEST1337", async (TcpClient client, string message) =>
{
    Console.WriteLine($"Received message: {message}");
    await server.SendMessage(client, "response1337", $"Response to: {message}");
});
```

### `SendMessage`

- **Description:** Sends a message to a specific client.
- **Arguments:**
  - `TcpClient client`: The client to which the message will be sent.
  - `string packetId`: The identifier to prepend to the message.
  - `string message`: The message to be sent to the client.
- **Returns:** `Task`: A `Task` representing the asynchronous operation. This method is asynchronous and returns a `Task` to indicate completion.

**Example**:
```
server.SendMessage(client, "TESTREQUEST1337", "Hello from the server");
```

## Variables

### `separator`

- **Description:** This variable defines the character used as a separator between the packet ID and the message content. The default separator is '|', but you can change it as needed.

**Example**:
```
await server.SendMessage(client, "TESTREQUEST1337", "Hello from the server");
```
