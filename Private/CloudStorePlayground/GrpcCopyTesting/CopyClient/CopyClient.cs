using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

using Grpc.Core;
using Google.Protobuf;

using Helloworld;
using System.Data;

namespace CopyClient
{
    public class CopyClient
    {
        public CopyClient(Channel channel)
        {
            this.client = new Copier.CopierClient(channel);
        }

        private readonly Copier.CopierClient client;

        // Maximum time to wait for the initial response
        public TimeSpan ResponseTimeout = TimeSpan.FromSeconds(5.0);

        // Bandwidth check govener. Switch out for different strategies.
        private BandwidthGovener govener = new FixedBandwidthGovener();

        // Accepting a stream factory instead of a stream allows the file
        // to be created only when content is actually available.
        public async Task<CopyResult> Read(string name, Func<Stream> streamFactory, CancellationToken ct = default(CancellationToken))
        {
            if (name is null) throw new ArgumentNullException(nameof(name));
            if (streamFactory is null) throw new ArgumentNullException(nameof(streamFactory));

            // Create a request object
            ReadRequest request = new ReadRequest()
            {
                FileName = name,
                Offset = 0,
                Compression = CopyCompression.None
                //Compression = CopyCompression.Gzip
            };

            // Create a result object
            CopyResult result = new CopyResult()
            {
                FileName = name,
                Status = CopyResultStatus.Successful
            };

            try
            {
                // Create a CancellationTokenSource we will use to cancel on time-outs
                // and to transmit any cancellation to server.
                using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    CallOptions options = default(CallOptions).WithCancellationToken(cts.Token);

                    // Get the initial response
                    Metadata responseHeaders;
                    Stopwatch responseTimer = Stopwatch.StartNew();
                    AsyncServerStreamingCall<Chunk> reply;
                    try
                    {
                        reply = client.Read(request, options);
                        responseHeaders = await reply.ResponseHeadersAsync.TimeoutAfter(ResponseTimeout, cts).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        result.ErrorType = e.GetType().Name;
                        result.ErrorMessage = e.Message;
                        if (e is TimeoutException)
                        {
                            result.Status = CopyResultStatus.ConnectionTimeout;
                        }
                        else
                        {
                            result.Status = CopyResultStatus.ConnectionFailure;
                        }
                        return result;
                    }
                    finally
                    {
                        responseTimer.Stop();
                        result.ResponseTime = responseTimer.Elapsed;
                    }

                    // Irritatingly, if server does not respond, we get
                    // no error on call or reading response headers,
                    // but instead exception when we start to read stream.
                    // We can avoid this by exiting early if we get no
                    // response headers.
                    if (responseHeaders.Count == 0)
                    {
                        result.Status = CopyResultStatus.ConnectionFailure;
                        return result;
                    }

                    // Extract response header data                    
                    ReadResponse response = ReadResponse.FromHeaders(responseHeaders);
                    if (response.ErrorType is object)
                    {
                        result.ErrorType = response.ErrorType;
                        result.ErrorMessage = response.ErrorMessage;
                        if (response.ErrorType == typeof(FileNotFoundException).Name)
                        {
                            result.Status = CopyResultStatus.FileNotFound;
                        }
                        else if (response.ErrorType == typeof(ThrottledException).Name)
                        {
                            result.Status = CopyResultStatus.RequestThrottled;
                        }
                        else
                        {
                            result.Status = CopyResultStatus.FileAccessErrorOnServer;
                        }
                        return result;
                    }
                    result.Compression = response.Compression;
                    long headerSize = response.FileSize;

                    if (result.Status != CopyResultStatus.Successful) return result;

                    // We got a successful response, so get a stream to write to
                    Stream writeStream;
                    try
                    {
                        writeStream = streamFactory();
                    }
                    catch (Exception e)
                    {
                        result.ErrorType = e.GetType().Name;
                        result.ErrorMessage = e.Message;
                        result.Status = CopyResultStatus.FileAccessErrorOnClient;
                        return result;
                    }

                    // Stream the content
                    long measuredSize = 0L;
                    using (writeStream)
                    {
                        // Prepare a content streamer and a bandwidth monitor that uses it   
                        //(GrpcContentStreamer streamer, Func<string> checker) = GetStreamerAndChecker();
                        GrpcContentStreamer streamer = new GrpcContentStreamer(govener);

                        // Stream the bytes
                        Stopwatch streamTimer = Stopwatch.StartNew();
                        try
                        {
                            switch (result.Compression)
                            {
                                case CopyCompression.None:
                                    await streamer.ReadContent(writeStream, reply.ResponseStream, cts.Token).ConfigureAwait(false);
                                    break;
                                case CopyCompression.Gzip:
                                    throw new NotImplementedException();
                                    //(chunks, bytes) = await ReadContentWithCompression(writeStream, reply.ResponseStream, cts.Token).TimeoutOnCondition(bandwidthCheckInterval, bandwidthCheck, cts).ConfigureAwait(false);
                            }
                        }
                        catch (Exception e)
                        {
                            result.ErrorType = e.GetType().Name;
                            result.ErrorMessage = e.Message;
                            if (e is TimeoutException)
                            {
                                result.Status = CopyResultStatus.StreamingTimeout;
                            }
                            else
                            {
                                result.Status = CopyResultStatus.StreamingFailure;
                            }
                            return result;
                        }
                        finally
                        {
                            streamTimer.Stop();
                            result.BytesStreamed = streamer.Bytes;
                            result.StreamingTime = streamTimer.Elapsed;
                        }

                        govener.Record(streamer.Bytes, streamTimer.Elapsed);
                        measuredSize = writeStream.Length;
                        Console.WriteLine($"responseTime = {result.ResponseTime} chunks = {streamer.Chunks} bytes = {streamer.Bytes} headerSize = {headerSize} measuredSize = {measuredSize} streamTime = {result.StreamingTime}");

                    }


                    return result;
                }

            }
            catch (Exception e)
            {
                result.ErrorType = e.GetType().Name;
                result.ErrorMessage = e.Message;
                result.Status = CopyResultStatus.OtherClientSideError;
                return result;
            }
        }

        private async Task<(long, long)> ReadContentWithCompression (Stream fileStream, IAsyncStreamReader<Chunk> replyStream, CancellationToken cts)
        {
            Debug.Assert(fileStream != null);
            Debug.Assert(replyStream != null);

            long chunks = 0L;
            long bytes = 0L;
            using (BufferedReadStream grpcStream = new BufferedReadStream(async () =>
            {
                if (await replyStream.MoveNext(cts).ConfigureAwait(false))
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
                using (Stream decompressedStream = new GZipStream(grpcStream, CompressionMode.Decompress, true))
                {
                    await decompressedStream.CopyToAsync(fileStream, CopyConstants.BufferSize, cts).ConfigureAwait(false);
                }
            }

            return (chunks, bytes);
        }

        public Task<CopyResult> Read (string name, Stream stream, CancellationToken ct = default(CancellationToken))
        {
            return Read(name, () => stream, ct);
        }

        public Task<CopyResult> Read (string name, CancellationToken ct = default(CancellationToken))
        {
            string path = Path.GetFileName(name);
            return Read(name, () => FileUtilities.OpenFileForWriting(path));
         }

        public async Task<CopyResult> Write(Stream stream, string name = null, CancellationToken ct = default(CancellationToken))
        {
            if (stream is null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new InvalidOperationException();

            WriteRequest request = new WriteRequest()
            {
                FileName = name
            };

            CopyResult result = new CopyResult()
            {
                FileName = name
            };

            try
            {
                Metadata requestHeaders = request.ToMetadata();

                // Create a CancellationTokenSource we will use to cancel on time-outs
                // and to transmit any cancellation to server.
                using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {

                    CallOptions options = default(CallOptions).WithHeaders(requestHeaders).WithCancellationToken(cts.Token);

                    AsyncClientStreamingCall<Chunk, WriteReply> call;
                    Stopwatch responseTimer = Stopwatch.StartNew();
                    try
                    {
                        call = client.Write(options);
                        //responseHeaders = await call.ResponseHeadersAsync.ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        // TODO: Also handle timeout
                        result.Status = CopyResultStatus.ConnectionFailure;
                        result.ErrorType = e.GetType().Name;
                        result.ErrorMessage = e.Message;
                        return result;
                    }
                    finally
                    {
                        responseTimer.Stop();
                        result.ResponseTime = responseTimer.Elapsed;
                    }

                    byte[] buffer = new byte[CopyConstants.BufferSize];
                    GrpcContentStreamer streamer = new GrpcContentStreamer(govener);

                    Stopwatch streamingTimer = Stopwatch.StartNew();
                    try
                    {
                        await streamer.WriteContent(stream, buffer, call.RequestStream, ct).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        result.ErrorType = e.GetType().Name;
                        result.ErrorMessage = e.Message;
                        if (e is TimeoutException)
                        {
                            result.Status = CopyResultStatus.StreamingTimeout;
                        }
                        else
                        {
                            result.Status = CopyResultStatus.StreamingFailure;
                        }
                        return result;
                    }
                    finally
                    {
                        streamingTimer.Stop();
                        result.BytesStreamed = streamer.Bytes;
                        result.StreamingTime = streamingTimer.Elapsed;
                    }

                    // gRPC requires client to explicitly declare end-of-stream
                    await call.RequestStream.CompleteAsync().ConfigureAwait(false);

                    WriteReply reply = await call.ResponseAsync;
                    result.FileName = reply.FileName;

                }

                return result;

            }
            catch (Exception e)
            {
                result.Status = CopyResultStatus.OtherClientSideError;
                result.ErrorType = e.GetType().Name;
                result.ErrorMessage = e.Message;
                return result;
            }
        }

        /// <summary>
        /// Writes the file given at the path to the server.
        /// </summary>
        /// <param name="path">The path of the file to write.</param>
        /// <returns>The result.</returns>
        public async Task<CopyResult> Write(string path, CancellationToken ct = default(CancellationToken)) 
        {
            // This must be implemented using async/await (rather than directly
            // returning a Task) so that Dispose is not called until Write has completed.
            using (Stream stream = FileUtilities.OpenFileForReading(path))
            {
                return await Write(stream, Path.GetFileName(path), ct).ConfigureAwait(false);
            }
        }

    }
}
