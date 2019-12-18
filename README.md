# Reproducer for [BedrockTransports issue 10](https://github.com/davidfowl/BedrockTransports/issues/10)

This is a sample server and client that can reliably reproduce a `System.NotSupportedException` when multiple processes
are attempting to talk to a BedrockFramework TCP server. The trace will usually look something like:

```
fail: Bedrock.Framework.EchoServerApplication[0]
      NotSupportedException
System.NotSupportedException:  The WriteAsync method cannot be called when another write operation is pending.
   at System.Net.Security.SslStream.WriteAsyncInternal[TWriteAdapter](TWriteAdapter writeAdapter, ReadOnlyMemory`1 buffer)
   at System.IO.Pipelines.StreamPipeWriter.FlushAsyncInternal(CancellationToken cancellationToken)
   at Bedrock.Framework.EchoServerApplication.HandleMessage(ConnectionContext connection, Byte[] msg) in EchoServerApplication.cs:line 63
```

The server processes incoming messages by dispatching each message and the connection it came from to a pool of worker
threads. An unbounded channel (`System.Threading.Channels`) is used as the concurrency-safe FIFO queue to dispatch work.

The client is a simple Python script that uses the `multiprocessing` class to spawn multiple processes and connect all
of them to the server at once; the issue is not as reproducible when threads are used, possibly because Python's global
interpreter lock (GIL) constricts client performance.

Observe that the client always reads a complete response before sending a new message. Theoretically there should never
be any conflicting write operations.
