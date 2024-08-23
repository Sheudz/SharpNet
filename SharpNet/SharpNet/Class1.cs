using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using SharpNet.Extensions;

namespace SharpNet
{
    public class SharpNet
    {
        private TcpListener _server;
        private bool _isRunning;
        private readonly ConcurrentDictionary<string, BlockingCollection<(TcpClient client, byte[] packet)>> _listeners;
        private readonly ConcurrentBag<BlockingCollection<(TcpClient client, byte[] packet)>> _generalListeners;
        private readonly ConcurrentBag<TcpClient> _connectedClients;
        public char separator = '|';

        public delegate void ClientDisconnectedHandler(TcpClient client);
        public event ClientDisconnectedHandler OnDisconnect;

        public SharpNet()
        {
            _listeners = new ConcurrentDictionary<string, BlockingCollection<(TcpClient, byte[])>>();
            _generalListeners = new ConcurrentBag<BlockingCollection<(TcpClient, byte[])>>();
            _connectedClients = new ConcurrentBag<TcpClient>();
        }

        public async void StartServer(int port)
        {
            if (_isRunning)
                throw new InvalidOperationException("Server is already running.");

            _server = new TcpListener(IPAddress.Any, port);
            _server.Start();
            _isRunning = true;

            while (_isRunning)
            {
                try
                {
                    var client = await _server.AcceptTcpClientAsync();
                    _connectedClients.Add(client);
                    HandleClient(client);
                }
                catch (SocketException)
                {
                    if (!_isRunning)
                        break;
                }
            }
        }

        public void StopServer()
        {
            if (!_isRunning)
                throw new InvalidOperationException("Server is not running.");

            _isRunning = false;

            _server.Stop();

            foreach (var client in _connectedClients)
            {
                if (client.Connected)
                {
                    client.Close();
                }
            }
        }

        public void Listen(string? packetid = null, TcpClient? specificClient = null, Action<TcpClient, string> callback = null!)
        {
            if (packetid == null)
            {
                var queue = new BlockingCollection<(TcpClient, byte[])>();
                _generalListeners.Add(queue);

                Task.Run(async () =>
                {
                    foreach (var (client, packet) in queue.GetConsumingEnumerable())
                    {
                        if (specificClient == null || client == specificClient)
                        {
                            string message = Encoding.UTF8.GetString(packet);
                            callback(client, message);
                        }
                    }
                });
            }
            else
            {
                var queue = _listeners.GetOrAdd(packetid, _ => new BlockingCollection<(TcpClient, byte[])>());

                Task.Run(async () =>
                {
                    foreach (var (client, packet) in queue.GetConsumingEnumerable())
                    {
                        if (specificClient == null || client == specificClient)
                        {
                            string message = ExtractMessageAfterPacketId(packet);
                            callback(client, message);
                        }
                    }
                });
            }
        }

        public async Task SendMessage(TcpClient client, string? packetid, string message)
        {
            if (client == null || !client.Connected)
                throw new InvalidOperationException("Client is not connected.");

            NetworkStream stream = client.GetStream();
            if (packetid != null) { message = $"{packetid}{separator}{message}"; }
            byte[] data = Encoding.UTF8.GetBytes(message);

            await stream.WriteAsync(data, 0, data.Length);
        }

        private async void HandleClient(TcpClient client)
        {
            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[1024];
            int bytesRead;

            try
            {
                while (_isRunning && (bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    string packetid = null;
                    bool isPacketIdFound = false;

                    try
                    {
                        packetid = ExtractPacketId(buffer, bytesRead);
                        isPacketIdFound = true;
                    }
                    catch (InvalidDataException){}

                    if (isPacketIdFound && _listeners.TryGetValue(packetid, out var queue))
                    {
                        byte[] packetData = new byte[bytesRead];
                        Array.Copy(buffer, packetData, bytesRead);
                        queue.Add((client, packetData));
                    }
                    else
                    {
                        foreach (var generalQueue in _generalListeners)
                        {
                            byte[] packetData = new byte[bytesRead];
                            Array.Copy(buffer, packetData, bytesRead);
                            generalQueue.Add((client, packetData));
                        }
                    }
                }
            }
            finally
            {
                client.TriggerDisconnect();
                OnDisconnect?.Invoke(client);
                client.Close();
            }
        }

        private string ExtractPacketId(byte[] buffer, int length)
        {
            string message = Encoding.UTF8.GetString(buffer, 0, length);
            int separatorIndex = message.IndexOf(separator);

            if (separatorIndex == -1)
            {
                throw new InvalidDataException("separator not found.");
            }

            string packetId = message.Substring(0, separatorIndex);

            return packetId;
        }

        private string ExtractMessageAfterPacketId(byte[] buffer)
        {
            string message = Encoding.UTF8.GetString(buffer);
            int separatorIndex = message.IndexOf(separator);

            if (separatorIndex == -1)
            {
                return string.Empty;
            }

            return message.Substring(separatorIndex + 1);
        }
    }
}
