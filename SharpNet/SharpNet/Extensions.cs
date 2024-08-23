using System;
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
}
