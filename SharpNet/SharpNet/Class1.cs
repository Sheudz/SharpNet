using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SharpNet
{
    public class SharpNet
    {
        private TcpListener _server;
        private bool _isRunning;
        private readonly ConcurrentDictionary<string, BlockingCollection<(TcpClient client, byte[] packet)>> _listeners;
        public char separator = '|';

        public SharpNet()
        {
            _listeners = new ConcurrentDictionary<string, BlockingCollection<(TcpClient, byte[])>>();
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
        }

        public void Listen(string packetid, Action<TcpClient, string> callback)
        {
            var queue = _listeners.GetOrAdd(packetid, _ => new BlockingCollection<(TcpClient, byte[])>());

            Task.Run(async () =>
            {
                foreach (var (client, packet) in queue.GetConsumingEnumerable())
                {
                    string message = ExtractMessageAfterPacketId(packet);
                    callback(client, message);
                }
            });
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

            while (_isRunning && (bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                string packetid = ExtractPacketId(buffer, bytesRead);
                if (_listeners.TryGetValue(packetid, out var queue))
                {
                    byte[] packetData = new byte[bytesRead];
                    Array.Copy(buffer, packetData, bytesRead);
                    queue.Add((client, packetData));
                }
            }

            client.Close();
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
