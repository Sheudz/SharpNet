using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Runtime.CompilerServices;

namespace SharpNet.Extensions
{
    public static class TcpClientExtensions
    {
        private static readonly ConditionalWeakTable<TcpClient, ClientEventHandlers> ClientEventTable =
            new ConditionalWeakTable<TcpClient, ClientEventHandlers>();

        public static void OnDisconnect(this TcpClient client, Action handler)
        {
            if (!ClientEventTable.TryGetValue(client, out var handlers))
            {
                handlers = new ClientEventHandlers();
                ClientEventTable.Add(client, handlers);
            }

            handlers.OnDisconnect += handler;
        }

        public static void TriggerDisconnect(this TcpClient client)
        {
            if (ClientEventTable.TryGetValue(client, out var handlers))
            {
                handlers.InvokeOnDisconnect();
            }
        }

        private class ClientEventHandlers
        {
            public event Action OnDisconnect;

            public void InvokeOnDisconnect()
            {
                OnDisconnect?.Invoke();
            }
        }
    }
    public class ListenerHandler
    {
        private readonly BlockingCollection<(TcpClient, byte[])> _queue;
        private readonly Task _listenerTask;
        private bool _isStopped;

        public ListenerHandler(BlockingCollection<(TcpClient, byte[])> queue, Task listenerTask)
        {
            _queue = queue;
            _listenerTask = listenerTask;
        }

        public void Stop()
        {
            if (!_isStopped)
            {
                _queue.CompleteAdding();
                _listenerTask.Wait();
                _isStopped = true;
            }
        }

        public bool IsRunning => !_isStopped && !_queue.IsCompleted;
    }

}
