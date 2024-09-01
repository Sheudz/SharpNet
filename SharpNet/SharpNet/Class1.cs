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
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _listenerCancellationTokens;
        public char separator = '|';

        public delegate void ClientDisconnectedHandler(TcpClient client);
        public event ClientDisconnectedHandler OnDisconnect;

        public SharpNet()
        {
            _listeners = new ConcurrentDictionary<string, BlockingCollection<(TcpClient, byte[])>>();
            _generalListeners = new ConcurrentBag<BlockingCollection<(TcpClient, byte[])>>();
            _connectedClients = new ConcurrentBag<TcpClient>();
            _listenerCancellationTokens = new ConcurrentDictionary<string, CancellationTokenSource>();
        }

        public Result Listen(string? packetid = null, TcpClient? specificClient = null, Action<TcpClient, string> callback = null!)
        {
            try
            {
                var cancellationTokenSource = new CancellationTokenSource();
                CancellationToken token = cancellationTokenSource.Token;

                if (packetid == null)
                {
                    var queue = new BlockingCollection<(TcpClient, byte[])>();
                    _generalListeners.Add(queue);

                    Task.Run(() =>
                    {
                        foreach (var (client, packet) in queue.GetConsumingEnumerable(token))
                        {
                            if (specificClient == null || client == specificClient)
                            {
                                string message = Encoding.UTF8.GetString(packet);
                                callback(client, message);
                            }
                        }
                    }, token);
                }
                else
                {
                    var queue = _listeners.GetOrAdd(packetid, _ => new BlockingCollection<(TcpClient, byte[])>());
                    _listenerCancellationTokens.TryAdd(packetid, cancellationTokenSource);

                    Task.Run(() =>
                    {
                        foreach (var (client, packet) in queue.GetConsumingEnumerable(token))
                        {
                            if (specificClient == null || client == specificClient)
                            {
                                string message = ExtractMessageAfterPacketId(packet);
                                callback(client, message);
                            }
                        }
                    }, token);
                }

                return Result.Ok("Listening for messages.");
            }
            catch (Exception ex)
            {
                return Result.Fail($"Failed to start listening: {ex.Message}");
            }
        }

        public Result StopListening(string? packetid = null)
        {
            try
            {
                if (packetid == null)
                {
                    foreach (var tokenSource in _listenerCancellationTokens.Values)
                    {
                        tokenSource.Cancel();
                    }
                    _listenerCancellationTokens.Clear();
                }
                else if (_listenerCancellationTokens.TryRemove(packetid, out var tokenSource))
                {
                    tokenSource.Cancel();
                }
                else
                {
                    return Result.Fail("Listener not found.");
                }

                return Result.Ok("Stopped listening.");
            }
            catch (Exception ex)
            {
                return Result.Fail($"Failed to stop listening: {ex.Message}");
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
