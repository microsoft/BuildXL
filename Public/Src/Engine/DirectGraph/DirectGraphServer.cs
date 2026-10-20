// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Net;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Grpc.Core;

namespace BuildXL.Engine.DirectGraph
{
    /// <summary>
    /// Hosts the DirectGraph protocol on a loopback gRPC endpoint.
    /// </summary>
    /// <remarks>
    /// Serves one graph: it accepts exactly one BuildGraph call and rejects any other. The DirectGraph resolver creates
    /// a server for each graph it constructs, starts it before it launches the emitter, and disposes it when the
    /// construction ends.
    /// </remarks>
    public sealed class DirectGraphServer : IDisposable
    {
        /// <summary>
        /// Maximum size, in bytes, of a single protocol message.
        /// </summary>
        /// <remarks>
        /// gRPC's default receive limit is 4 MB, which a single process pip with very many inputs or outputs can exceed. A
        /// client that limits the size of what it sends has to allow this much.
        /// </remarks>
        public const int MaxMessageLengthBytes = 256 * 1024 * 1024;

        /// <summary>
        /// Channel options carrying the message limits. Used by the server, and exposed so a client can configure its
        /// channel with the same values.
        /// </summary>
        public static IEnumerable<ChannelOption> MessageLengthChannelOptions { get; } = new[]
        {
            new ChannelOption(Grpc.Core.ChannelOptions.MaxReceiveMessageLength, MaxMessageLengthBytes),
            new ChannelOption(Grpc.Core.ChannelOptions.MaxSendMessageLength, MaxMessageLengthBytes),
        };

        private readonly Server m_server;
        private readonly ServiceImplementation m_service;
        private readonly Lazy<Task> m_shutdown;

        /// <summary>
        /// Bound TCP port. Valid after <see cref="Start"/>.
        /// </summary>
        public int Port { get; private set; }

        /// <summary>
        /// Endpoint that should be passed to a DirectGraph client.
        /// </summary>
        public string Endpoint => $"{IPAddress.Loopback}:{Port}";

        /// <summary>
        /// Completes once the Complete operation has been handled and its response written. Never completes if the call
        /// ends without Complete or fails first.
        /// </summary>
        public Task Completion => m_service.Completion;

        /// <nodoc />
        /// <param name="graphBuilder">Receives the operations.</param>
        /// <param name="onOperationReceived">
        /// Invoked as each operation arrives, before it is handled. Lets the caller track how long the emitter has
        /// been silent.
        /// </param>
        public DirectGraphServer(IDirectGraphGraphBuilder graphBuilder, Action<GraphOperation> onOperationReceived = null)
        {
            Contract.Requires(graphBuilder != null);

            m_service = new ServiceImplementation(graphBuilder, onOperationReceived);
            m_server = new Server(MessageLengthChannelOptions)
            {
                Services = { DirectGraphService.BindService(m_service) },
            };
            m_shutdown = new Lazy<Task>(() => m_server.KillAsync());
        }

        /// <summary>
        /// Starts listening. A port of zero asks the operating system to select an available port.
        /// </summary>
        public void Start(int port = 0)
        {
            var serverPort = new ServerPort(IPAddress.Loopback.ToString(), port, ServerCredentials.Insecure);
            m_server.Ports.Add(serverPort);
            m_server.Start();
            Port = m_server.Ports.Single().BoundPort;
            if (Port == 0)
            {
                throw new InvalidOperationException("The DirectGraph gRPC server started without a valid bound port.");
            }
        }

        /// <summary>
        /// Closes the endpoint and cancels any call still in flight, so a client that does not end its call cannot keep
        /// this from returning. Can be called more than once.
        /// </summary>
        public Task ShutdownAsync() => m_shutdown.Value;

        /// <inheritdoc />
        public void Dispose() => ShutdownAsync().GetAwaiter().GetResult();

        private sealed class ServiceImplementation : DirectGraphService.DirectGraphServiceBase
        {
            // Kept short on purpose: operations decoded far ahead only wait in memory.
            private const int ReadAheadOperations = 16;
            private const int PendingResponses = 64;

            private readonly IDirectGraphGraphBuilder m_graphBuilder;
            private readonly Action<GraphOperation> m_onOperationReceived;
            private readonly TaskCompletionSource<bool> m_completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private int m_calls;

            public Task Completion => m_completion.Task;

            public ServiceImplementation(IDirectGraphGraphBuilder graphBuilder, Action<GraphOperation> onOperationReceived)
            {
                m_graphBuilder = graphBuilder;
                m_onOperationReceived = onOperationReceived;
            }

            public override async Task BuildGraph(
                IAsyncStreamReader<GraphOperation> requestStream,
                IServerStreamWriter<GraphOperationResponse> responseStream,
                ServerCallContext context)
            {
                // The builder keeps the operation and value ids of the stream it serves, so a graph is built over exactly
                // one call. A second one, concurrent or not, would mix its ids with the first one's.
                if (Interlocked.Increment(ref m_calls) > 1)
                {
                    throw new RpcException(new Status(
                        StatusCode.FailedPrecondition,
                        "This DirectGraph server has already accepted a BuildGraph call. A graph is built over exactly one stream."));
                }

                // Reading, handling and responding overlap, so an emitter that sends ahead is not held up by decoding or
                // by writing responses. Handling itself stays strictly sequential and in stream order.
                var operations = System.Threading.Channels.Channel.CreateBounded<GraphOperation>(
                    new BoundedChannelOptions(ReadAheadOperations) { SingleReader = true, SingleWriter = true });
                var responses = System.Threading.Channels.Channel.CreateBounded<(GraphOperationResponse Response, bool AnswersComplete)>(
                    new BoundedChannelOptions(PendingResponses) { SingleReader = true, SingleWriter = true });

                // A failure in any stage stops the other two.
                using var pipeline = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
                var reading = ReadAsync(requestStream, operations.Writer, pipeline.Token);
                var writing = WriteAsync(responseStream, responses.Reader, pipeline);
                bool completed = false;
                Exception handlingFailure = null;

                try
                {
                    // Once Complete's response is written nothing reads or writes the call any more, so an emitter tearing
                    // the call down as soon as it has that response cannot turn a finished graph into a failure.
                    while (!completed && await operations.Reader.WaitToReadAsync(pipeline.Token))
                    {
                        while (!completed && operations.Reader.TryRead(out var operation))
                        {
                            var response = await m_graphBuilder.HandleOperationAsync(operation, pipeline.Token);
                            completed = operation.OperationCase == GraphOperation.OperationOneofCase.Complete;
                            await responses.Writer.WriteAsync((response, completed), pipeline.Token);
                        }
                    }
                }
                catch (Exception e)
                {
                    handlingFailure = e;
                    await pipeline.CancelAsync();
                }

                responses.Writer.TryComplete();

                // Both stages use the call's streams, so neither may outlive this method.
                Exception writingFailure = await GetFailureAsync(writing);
                await reading;

                // Report what went wrong rather than the cancellation it caused in the other stages.
                var failure = handlingFailure is OperationCanceledException && writingFailure != null ? writingFailure : handlingFailure ?? writingFailure;
                if (failure != null)
                {
                    ExceptionDispatchInfo.Capture(failure).Throw();
                }

                // Every response, Complete's included, is on the wire before completion is signalled.
                if (completed)
                {
                    m_completion.TrySetResult(true);
                }
            }

            private static async Task<Exception> GetFailureAsync(Task task)
            {
                try
                {
                    await task;
                    return null;
                }
                catch (Exception e)
                {
                    return e;
                }
            }

            /// <summary>
            /// Never faults: a failure, including cancellation, completes the operations channel with it instead.
            /// </summary>
            private async Task ReadAsync(
                IAsyncStreamReader<GraphOperation> requestStream,
                ChannelWriter<GraphOperation> operations,
                CancellationToken cancellationToken)
            {
                try
                {
                    while (await requestStream.MoveNext(cancellationToken))
                    {
                        var operation = requestStream.Current;
                        m_onOperationReceived?.Invoke(operation);

                        await operations.WriteAsync(operation, cancellationToken);

                        // Nothing may follow Complete, so the stream is not read past it.
                        if (operation.OperationCase == GraphOperation.OperationOneofCase.Complete)
                        {
                            break;
                        }
                    }

                    operations.TryComplete();
                }
                catch (Exception e)
                {
                    operations.TryComplete(e);
                }
            }

            private static async Task WriteAsync(
                IServerStreamWriter<GraphOperationResponse> responseStream,
                ChannelReader<(GraphOperationResponse Response, bool AnswersComplete)> responses,
                CancellationTokenSource pipeline)
            {
                try
                {
                    while (await responses.WaitToReadAsync(pipeline.Token))
                    {
                        while (responses.TryRead(out var pending))
                        {
                            await responseStream.WriteAsync(pending.Response);
                            if (pending.AnswersComplete)
                            {
                                return;
                            }
                        }
                    }
                }
                catch
                {
                    await pipeline.CancelAsync();
                    throw;
                }
            }
        }
    }
}
