using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
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
            using var handler = new EchoConnectionHandler(connection, _pool, _logger);
            await handler.Handle();
        }

        private async Task HandleMessage(EchoConnectionHandler connection, byte[] msg)
        {
            await connection.WriteAsync(msg);
        }

        private async void HandleMessage(WorkItem item) =>
            await HandleMessage(item.Connection, item.Message);
    }

    internal class EchoConnectionHandler : IDisposable
    {
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1);
        private readonly ConnectionContext _connection;
        private readonly ILogger<EchoServerApplication> _logger;
        private readonly WorkerPool<WorkItem> _pool;
        private bool _disposed = false;
        private bool _disposing = false;

        public EchoConnectionHandler(ConnectionContext connection, WorkerPool<WorkItem> pool, ILogger<EchoServerApplication> logger)
        {
            _connection = connection;
            _pool = pool;
            _logger = logger;
        }

        ~EchoConnectionHandler()
        {
            Dispose();
        }

        public void Dispose()
        {
            if (!_disposed && !_disposing)
            {
                _disposing = true;

                _semaphore.Dispose();

                _disposed = true;
                _disposing = false;
            }
        }

        public async Task Handle()
        {
            try
            {
                _logger.LogInformation("{ConnectionId} connected", _connection.ConnectionId);

                while (true)
                {
                    var result = await _connection.Transport.Input.ReadAsync();
                    if (result.IsCompleted)
                    {
                        break;
                    }
                    var buffer = result.Buffer;

                    foreach (var msg in buffer)
                    {
                        var item = new WorkItem()
                        {
                            Connection = this,
                            Message = msg.ToArray()
                        };

                        await _pool.Enqueue(item);

                        // This does not produce the issue.
                        // await HandleMessage(item);
                    }

                    _connection.Transport.Input.AdvanceTo(buffer.End);
                }
            }
            finally
            {
                _logger.LogInformation("{ConnectionId} disconnected", _connection.ConnectionId);
            }
        }

        public async Task WriteAsync(byte[] msg)
        {
            await _semaphore.WaitAsync();
            try
            {
                await _connection.Transport.Output.WriteAsync(msg);
            }
            catch (NotSupportedException e)
            {
                _logger.LogError(e, "NotSupportedException");
            }
            finally
            {
                _semaphore.Release();
            }
        }
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
        public EchoConnectionHandler Connection;
        public byte[] Message;
    }
}
