using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Grpc.Core;
using Google.Protobuf;

using Helloworld;
using System;
using Grpc.Core.Logging;

namespace CopyClient
{

    internal class GrpcContentStreamer
    {
        public GrpcContentStreamer() : this(null) { }

        public GrpcContentStreamer(BandwidthGovener govener)
        {
            this.govener = govener;
        }

        public readonly BandwidthGovener govener;

        private int chunks = 0;
        private long bytes = 0L;
        private long previousBytes = 0L;

        public long Bytes { get => bytes; }

        public int Chunks { get => chunks; }

        private Timer timer;

        private CancellationTokenSource cts;

        private TaskCompletionSource<long> tcs;

        private void TimerCallback (object state)
        {
            long currentBytes = bytes;
            double bandwidth = (currentBytes - previousBytes) / govener.CheckInterval.TotalSeconds;
            Console.WriteLine($"measured bandwidth {currentBytes} - {previousBytes} / {govener.CheckInterval.TotalSeconds} = {bandwidth}");
            if (bandwidth <= govener.MinimumBytesPerSecond)
            {
                Exception timeoutException = new TimeoutException();
                tcs.TrySetException(timeoutException);
                try
                {
                    cts.Cancel();
                }
                catch (ObjectDisposedException) { }
            }
            else
            {
                timer.Change(govener.CheckInterval, Timeout.InfiniteTimeSpan);
            }

        }

        public async Task<long> ReadContent(Stream targetStream, IAsyncStreamReader<Chunk> replyStream, CancellationToken ct)
        {
            if (govener is null || !govener.IsActive) return await ReadContentCore(targetStream, replyStream, ct);

            Debug.Assert(!(govener is null));

            cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            tcs = new TaskCompletionSource<long>();
            timer = new Timer(TimerCallback, null, govener.CheckInterval, Timeout.InfiniteTimeSpan);
            Task<long> readTask = ReadContentCore(targetStream, replyStream, cts.Token);
            await readTask.ContinueWith(t =>
            {
                timer.Dispose();
                cts.Dispose();
                switch (t.Status)
                {
                    case TaskStatus.RanToCompletion:
                        tcs.TrySetResult(t.Result);
                        break;
                    case TaskStatus.Faulted:
                        tcs.TrySetException(t.Exception);
                        break;
                    case TaskStatus.Canceled:
                        tcs.TrySetCanceled();
                        break;
                }
            }, TaskContinuationOptions.ExecuteSynchronously);
            return await tcs.Task; 
        }


        private async Task<long> ReadContentCore (Stream targetStream, IAsyncStreamReader<Chunk> replyStream, CancellationToken ct)
        {
            Debug.Assert(targetStream is object);
            Debug.Assert(targetStream.CanWrite);
            Debug.Assert(replyStream is object);
            while (await replyStream.MoveNext(ct).ConfigureAwait(false))
            {
                chunks++;
                Chunk chunk = replyStream.Current;
                bytes += chunk.Content.Length;
                chunk.Content.WriteTo(targetStream);
            }
            return bytes;
        }

        public async Task<long> WriteContent(Stream sourceStream, byte[] buffer, IAsyncStreamWriter<Chunk> writer, CancellationToken ct)
        {
            Debug.Assert(sourceStream is object);
            Debug.Assert(sourceStream.CanRead);
            Debug.Assert(buffer is object);
            Debug.Assert(writer is object);

            while (true)
            {
                int count = await sourceStream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false);
                if (count == 0) return bytes;

                ByteString content = ByteString.CopyFrom(buffer, 0, count);
                Chunk chunk = new Chunk() { Content = content, Index = chunks };
                await writer.WriteAsync(chunk);

                chunks++;
                bytes += count;
            }
        }

    }
}
