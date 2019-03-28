using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

using Grpc.Core;

using Helloworld;

namespace CopyClient
{
    public class CopyClient
    {
        public CopyClient (Channel channel)
        {
            this.client = new Copier.CopierClient(channel);
        }

        private readonly Copier.CopierClient client;

        // Providing stream factory instead of stream allows stream
        // to be created only when content is actually available.
        public async Task Copy (string name, Func<Stream> streamFactory)
        {
            CopyRequest request = new CopyRequest()
            {
                Name = name,
                Offset = 0,
                Compression = CopyCompression.Gzip
            };

            bool success = true;
            long headerSize = -1;
            CopyCompression compression = CopyCompression.None;
            Stopwatch responseTimer = Stopwatch.StartNew();
            AsyncServerStreamingCall<CopyReply> reply = client.Copy(request);
            Metadata data = await reply.ResponseHeadersAsync.ConfigureAwait(false);
            responseTimer.Stop();
            foreach (Metadata.Entry header in data)
            {
                Console.WriteLine($"{header.Key}={header.Value}");
                if (String.Compare(header.Key, "exception", StringComparison.InvariantCultureIgnoreCase) == 0) success = false;
                if (String.Compare(header.Key, "fileSize", StringComparison.InvariantCultureIgnoreCase) == 0) headerSize = Int64.Parse(header.Value);
                if (String.Compare(header.Key, "compression", StringComparison.InvariantCultureIgnoreCase) == 0) compression = Enum.Parse<CopyCompression>(header.Value);
            }
            if (!success) return;

            long chunks = 0L;
            long bytes = 0L;
            Stopwatch streamTimer;
            long measuredSize = 0L;
            using (Stream writeStream = streamFactory())
            {
                streamTimer = Stopwatch.StartNew();
                switch (compression)
                {
                    case CopyCompression.None:
                        (chunks, bytes) = await StreamContent(writeStream, reply.ResponseStream).ConfigureAwait(false);
                        break;
                    case CopyCompression.Gzip:
                        (chunks, bytes) = await StreamContentWithCompression(writeStream, reply.ResponseStream).ConfigureAwait(false);
                        break;
                }
                streamTimer.Stop();

                measuredSize = writeStream.Length;
            }

            Console.WriteLine($"responseTime = {responseTimer.ElapsedMilliseconds} chunks = {chunks} bytes = {bytes} headerSize = {headerSize} measuredSize = {measuredSize} streamTime = {streamTimer.ElapsedMilliseconds}");

        }

        private async Task<(long,long)> StreamContent (Stream fileStream, IAsyncStreamReader<CopyReply> replyStream)
        {
            long chunks = 0L;
            long bytes = 0L;
            while (await replyStream.MoveNext(CancellationToken.None).ConfigureAwait(false))
            {
                chunks++;
                CopyReply oneOfManyReply = replyStream.Current;
                bytes += oneOfManyReply.Content.Length;
                oneOfManyReply.Content.WriteTo(fileStream);
            }
            return (chunks,bytes);
        }

        private async Task<(long, long)> StreamContentWithCompression (Stream fileStream, IAsyncStreamReader<CopyReply> replyStream)
        {
            Debug.Assert(fileStream != null);
            Debug.Assert(replyStream != null);

            long chunks = 0L;
            long bytes = 0L;
            using (BufferedReadStream grpcStream = new BufferedReadStream(async () =>
            {
                if (await replyStream.MoveNext(CancellationToken.None).ConfigureAwait(false))
                {
                    chunks++;
                    bytes += replyStream.Current.Content.Length;
                    return replyStream.Current.Content.ToByteArray();
                }
                else
                {
                    return null;
                }
            }))
            {
                //using (Stream decompressedStream = grpcStream)
                using (Stream decompressedStream = new GZipStream(grpcStream, CompressionMode.Decompress, true))
                {
                    await decompressedStream.CopyToAsync(fileStream, CopyConstants.BufferSize).ConfigureAwait(false);
                }
            }

            return (chunks, bytes);
        }

        public async Task Copy (string name, Stream stream)
        {
            await Copy(name, () => stream).ConfigureAwait(false);
        }

        public async Task Copy (string name)
        {
            // FileMode.Create truncates if file already exists, which is what we want.
            string localName = Path.GetFileName(name);
            await Copy(name, () => new FileStream(localName, FileMode.Create, FileAccess.Write, FileShare.None, CopyConstants.BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan)).ConfigureAwait(false);
         }

    }
}
