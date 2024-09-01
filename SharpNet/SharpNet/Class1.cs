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

        public async Task<Result> StartServer(int port)
        {
            if (_isRunning)
                return Result.Fail("Server is already running.");

            try
            {
                _server = new TcpListener(IPAddress.Any, port);
                _server.Start();
                _isRunning = true;

                while (_isRunning)
                {
                    var client = await _server.AcceptTcpClientAsync();
                    _connectedClients.Add(client);
                    HandleClient(client);
                }

                return Result.Ok("Server started successfully.");
            }
            catch (Exception ex)
            {
                return Result.Fail($"Failed to start server: {ex.Message}");
            }
        }

        public Result StopServer()
        {
            if (!_isRunning)
                return Result.Fail("Server is not running.");

            try
            {
                _isRunning = false;
                _server.Stop();

                foreach (var client in _connectedClients)
                {
                    if (client.Connected)
                    {
                        client.Close();
                    }
                }

                return Result.Ok("Server stopped successfully.");
            }
            catch (Exception ex)
            {
                return Result.Fail($"Failed to stop server: {ex.Message}");
            }
        }

        public ListenerHandler Listen(string? packetid = null, TcpClient? specificClient = null, Action<TcpClient, string> callback = null!)
        {
            try
            {
                BlockingCollection<(TcpClient, byte[])> queue;

                if (packetid == null)
                {
                    queue = new BlockingCollection<(TcpClient, byte[])>();
                    _generalListeners.Add(queue);
                }
                else
                {
                    queue = _listeners.GetOrAdd(packetid, _ => new BlockingCollection<(TcpClient, byte[])>());
                }

                var listenerTask = Task.Run(() =>
                {
                    foreach (var (client, packet) in queue.GetConsumingEnumerable())
                    {
                        if (specificClient == null || client == specificClient)
                        {
                            string message = packetid == null ?
                                             Encoding.UTF8.GetString(packet) :
                                             ExtractMessageAfterPacketId(packet);
                            callback(client, message);
                        }
                    }
                });

                return new ListenerHandler(queue, listenerTask);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to start listening: {ex.Message}");
            }
        }


        public async Task<Result> SendMessage(TcpClient client, string? packetid, string message)
        {
            if (client == null || !client.Connected)
                return Result.Fail("Client is not connected.");

            try
            {
                NetworkStream stream = client.GetStream();
                if (packetid != null) { message = $"{packetid}{separator}{message}"; }
                byte[] data = Encoding.UTF8.GetBytes(message);

                await stream.WriteAsync(data, 0, data.Length);
                return Result.Ok("Message sent successfully.");
            }
            catch (Exception ex)
            {
                return Result.Fail($"Failed to send message: {ex.Message}");
            }
        }

        private async void HandleClient(TcpClient client)
        {
            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[1024];
            int bytesRead;

            try
            {
                while (_isRunning && client.Connected && (bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    string packetid = null;
                    bool isPacketIdFound = false;

                    try
                    {
                        packetid = ExtractPacketId(buffer, bytesRead);
                        isPacketIdFound = true;
                    }
                    catch (InvalidDataException) { }

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
            catch (IOException ioEx)
            {
                Console.WriteLine($"IOException: {ioEx.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception: {ex.Message}");
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
                throw new InvalidDataException("Separator not found.");
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


    public class Result
    {
        public bool Success { get; }
        public string Message { get; }

        public Result(bool success, string message)
        {
            Success = success;
            Message = message;
        }

        public static Result Ok(string message = "successfully")
        {
            return new Result(true, message);
        }

        public static Result Fail(string message)
        {
            return new Result(false, message);
        }
    }
}
