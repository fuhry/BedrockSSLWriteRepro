using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Bedrock.Framework.Middleware.Tls;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Logging;

namespace Bedrock.Framework
{
    public class EchoServerApplication : ConnectionHandler
    {
        private readonly ILogger<EchoServerApplication> _logger;
        private readonly WorkerPool<WorkItem> _pool;

        public EchoServerApplication(ILogger<EchoServerApplication> logger)
        {
            _logger = logger;
            _pool = WorkerPool<WorkItem>.Instance(HandleMessage, _logger);
        }

        public override async Task OnConnectedAsync(ConnectionContext connection)
        {
            try
            {
                _logger.LogInformation("{ConnectionId} connected", connection.ConnectionId);

                while (true)
                {
                    var result = await connection.Transport.Input.ReadAsync();
                    if (result.IsCompleted)
                    {
                        break;
                    }
                    var buffer = result.Buffer;

                    foreach (var msg in buffer)
                    {
                        var item = new WorkItem()
                        {
                            Connection = connection,
                            Message = msg.ToArray()
                        };

                        await _pool.Enqueue(item);

                        // This does not produce the issue.
                        // await HandleMessage(item);
                    }

                    connection.Transport.Input.AdvanceTo(buffer.End);
                }
            }
            finally
            {
                _logger.LogInformation("{ConnectionId} disconnected", connection.ConnectionId);
            }
        }

        private async Task HandleMessage(ConnectionContext connection, byte[] msg)
        {
            try
            {
                await connection.Transport.Output.WriteAsync(msg);
            }
            catch (NotSupportedException e)
            {
                _logger.LogError(e, "NotSupportedException");
            }
        }

        private async void HandleMessage(WorkItem item) =>
            await HandleMessage(item.Connection, item.Message);
    }

    internal class WorkerPool<T>
    {
        private const int WORKER_COUNT = 8;
        private Thread[] _workers = { };
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly Channel<T> _work = Channel.CreateUnbounded<T>();
        private Action<T> _handler;
        private ILogger<EchoServerApplication> _logger;

        private static WorkerPool<T> _instance;

        private WorkerPool(Action<T> handler, ILogger<EchoServerApplication> logger)
        {
            _logger = logger;

            _handler = handler;

            for (int i = 0; i < WORKER_COUNT; i++)
            {
                var worker = new Thread(Run);
                worker.Start();
                _workers.Append(worker);
            }
        }

        ~WorkerPool()
        {
            _cts.Cancel();
            foreach (var worker in _workers)
            {
                worker.Join();
            }
        }

        public static WorkerPool<T> Instance(Action<T> handler, ILogger<EchoServerApplication> logger)
        {
            if (_instance == null)
            {
                _instance = new WorkerPool<T>(handler, logger);
            }

            return _instance;
        }

        public async Task Enqueue(T item)
        {
            await _work.Writer.WriteAsync(item);
        }

        private async void Run()
        {
            _logger.LogDebug("Starting worker");
            while (!_cts.Token.IsCancellationRequested)
            {
                var msg = await _work.Reader.ReadAsync();
                _handler.Invoke(msg);
            }
            _logger.LogDebug("Exiting worker");
        }
    }

    internal class WorkItem
    {
        public ConnectionContext Connection;
        public byte[] Message;
    }
}
