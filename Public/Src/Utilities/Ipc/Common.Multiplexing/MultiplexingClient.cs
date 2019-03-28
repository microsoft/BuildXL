// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BuildXL.Ipc.Interfaces;
using BuildXL.Utilities;
using BuildXL.Utilities.Tasks;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Ipc.Common.Multiplexing
{
    /// <summary>
    /// Used for multiplexing requests from multiple clients over a single connection stream.
    ///
    /// Implements two data folows: (1) receiving, and (2) sending.
    ///
    /// Receiving Dataflow:
    ///
    ///                                  +---------------+
    ///                              1   | GenericServer |   1
    ///     ReceiveResponseAsync() --/-->|  {Response}   |---/--> SetResponse()
    ///                                  +---------------+
    ///
    /// Sending Dataflow:
    ///
    ///                  +-------------+
    ///              n   | ActionBlock |   1
    ///     Send() --/-->|  {Request}  |---/--> SendRequestAsync()
    ///                  +-------------+
    ///
    /// See <see cref="Completion"/> for remakrs about when this client is considered completed.
    ///
    /// Receives a live connection stream to a server in its constructor.
    /// </summary>
    public sealed class MultiplexingClient : IClient
    {
        private readonly Stream m_stream;

        private readonly ActionBlock<Request> m_sendRequestBlock;
        private readonly GenericServer<Response> m_responseListener;
        private readonly Task m_completion;

        // maps Request.Id to its corresponding task source
        private readonly ConcurrentDictionary<int, TaskSourceSlim<IIpcResult>> m_pendingRequests;

        private bool m_disconnectRequestedByServer;

        private ILogger Logger { get; }

        /// <inheritdoc />
        public IClientConfig Config { get; }

        /// <nodoc />
        public MultiplexingClient(IClientConfig config, Stream stream)
        {
            Contract.Requires(config != null);
            Contract.Requires(stream != null);

            Config = config;
            Logger = config.Logger ?? VoidLogger.Instance;
            m_stream = stream;

            m_pendingRequests = new ConcurrentDictionary<int, TaskSourceSlim<IIpcResult>>();
            m_disconnectRequestedByServer = false;

            // Receiving Dataflow:
            //   This generic server will call 'SetResponse' for every received Response.  'SetResponse'
            //   could be done in parallel, but there is no point, since they all access a shared concurrent
            //   dictionary and all they do is lookup task completion source and set a result for it.
            m_responseListener = new GenericServer<Response>(
                name: "MultiplexingClient.ResponseListener",
                config: new ServerConfig { Logger = Logger, MaxConcurrentClients = 1 },
                listener: ReceiveResponseAsync,
                clientFailuresAreFatal: true);

            // Sending Dataflow:
            //   All 'Send' requests are concurrently queued up here. Processing of this block
            //   ('SendRequestAsync') must be sequential, because uses the shared connection.
            m_sendRequestBlock = new ActionBlock<Request>(
                SendRequestAsync,
                new ExecutionDataflowBlockOptions
                {
                    MaxDegreeOfParallelism = 1,
                });

            // set continuations that handle errors (unblock pending requests and set failure as completion of this client)
            var continuationDone = TaskSourceSlim.Create<Unit>();
            m_responseListener.Completion.ContinueWith(_ => m_sendRequestBlock.Complete());
            m_sendRequestBlock.Completion.ContinueWith(async _ =>
            {
                m_responseListener.Complete();

                UnblockPendingRequests(new IpcResult(IpcResultStatus.GenericError, "MultiplexingClient completed before all requests were handled"));
                if (!m_disconnectRequestedByServer)
                {
                    var succeeded = await Request.StopRequest.TrySerializeAsync(m_stream);
                    Logger.Verbose("Sending StopRequest {0}.", succeeded ? "Succeeded" : "Failed");
                }

                continuationDone.SetResult(Unit.Void);
            });

            // start listening for responses
            m_responseListener.Start(SetResponseAsync);

            // set the completion task
            m_completion = TaskUtilities.SafeWhenAll(new[] { m_sendRequestBlock.Completion, m_responseListener.Completion, continuationDone.Task });
        }

        /// <summary>
        /// This client completes when both dataflows complete.  If any of the two dataflows fails,
        /// this client fails too with the same error.  If there are any pending requests when a
        /// failure is encountered, they are all terminated with a generic error.
        /// </summary>
        public Task Completion => m_completion;

        /// <inheritdoc />
        public void Dispose()
        {
            m_responseListener.Dispose();
            m_stream.Dispose();
        }

        /// <inheritdoc />
        public Task<IIpcResult> Send(IIpcOperation operation)
        {
            Contract.Requires(operation != null);

            var request = new Request(operation);

            // Must add the request to the m_pendingRequest dictionary before posting it to m_sendRequestBlock
            // Otherwise, the following can happen:
            //   1) the request is posted
            //   2) the request is picked up from the queue and processed
            //   3) the request handler looks up corresponding completionSource in the dictionary which is not there yet (ERROR)
            //   4) the TaskCompletionSource is added to the dictionary
            var completionSource = TaskSourceSlim.Create<IIpcResult>();
            m_pendingRequests[request.Id] = completionSource;

            operation.Timestamp.Request_BeforePostTime = DateTime.UtcNow;
            bool posted = m_sendRequestBlock.Post(request);
            if (!posted)
            {
                // if the request was not posted:
                // (1) remove it from the dictionary
                TaskSourceSlim<IIpcResult> src;
                m_pendingRequests.TryRemove(request.Id, out src);

                // (2) complete it (with TransmissionError)
                completionSource.TrySetResult(new IpcResult(
                    IpcResultStatus.TransmissionError,
                    "Could not post IPC request: the client has already terminated."));
            }

            return completionSource.Task;
        }

        /// <inheritdoc />
        public void RequestStop()
        {
            RequestStop("Stop requested");
        }

        private void RequestStop(string logMessage)
        {
            Logger.Verbose(logMessage);
            m_responseListener.Complete();
            m_sendRequestBlock.Complete();
        }

        private async Task SendRequestAsync(Request request)
        {
            request.Operation.Timestamp.Request_BeforeSendTime = DateTime.UtcNow;
            Logger.Verbose("Sending request...");
            await request.SerializeAsync(m_stream);
            Logger.Verbose("Request sent: " + request);
            request.Operation.Timestamp.Request_AfterSendTime = DateTime.UtcNow;

            if (!request.Operation.ShouldWaitForServerAck)
            {
                await SetResponseAsync(new Response(request.Id, IpcResult.Success()));
            }

            request.Operation.Timestamp.Request_AfterServerAckTime = DateTime.UtcNow;
        }

        private async Task<Response> ReceiveResponseAsync(CancellationToken token)
        {
            DateTime beforeDeserialize = DateTime.UtcNow;
            Logger.Verbose("Deserializing response...");
            var response = await Response.DeserializeAsync(m_stream, token);
            Logger.Verbose("Response received: " + response);
            response.Result.Timestamp.Response_BeforeDeserializeTime = beforeDeserialize;
            response.Result.Timestamp.Response_AfterDeserializeTime = DateTime.UtcNow;

            if (response.IsDisconnectResponse)
            {
                m_disconnectRequestedByServer = true;
                RequestStop("Request to disconnect received.");
            }

            return response;
        }

        private Task SetResponseAsync(Response response)
        {
            response.Result.Timestamp.Response_BeforeSetTime = DateTime.UtcNow;

            if (response.IsDisconnectResponse)
            {
                return Unit.VoidTask;
            }

            var maybeSetResponse = TryFindTaskForRequest(response.RequestId)
                .Then(taskSource => TrySetResult(taskSource, response));

            if (!maybeSetResponse.Succeeded)
            {
                throw new IpcException(
                    IpcException.IpcExceptionKind.SpuriousResponse,
                    I($"Could not set response '{response}'. Reason: {maybeSetResponse.Failure.Describe()}"));
            }

            return Unit.VoidTask;
        }

        private Possible<TaskSourceSlim<IIpcResult>> TryFindTaskForRequest(int requestId)
        {
            Logger.Verbose("Looking up request id {0}", requestId);
            TaskSourceSlim<IIpcResult> taskSource;
            if (m_pendingRequests.TryRemove(requestId, out taskSource))
            {
                Logger.Verbose("Request {0} found", requestId);
                return taskSource;
            }
            else
            {
                Logger.Verbose("Request {0} not found", requestId);
                return new Failure<string>("Could not find pending request with ID = " + requestId);
            }
        }

        private Possible<TaskSourceSlim<IIpcResult>> TrySetResult(TaskSourceSlim<IIpcResult> taskSource, Response response)
        {
            response.Result.Timestamp.Response_AfterSetTime = DateTime.UtcNow;
            if (taskSource.TrySetResult(response.Result))
            {
                Logger.Verbose("Setting response for request id {0} succeeded", response.RequestId);
                return taskSource;
            }
            else
            {
                Logger.Verbose("Setting response for request id {0} failed", response.RequestId);
                return new Failure<string>("Result was already set for request ID = " + response.RequestId);
            }
        }

        private void UnblockPendingRequests(IIpcResult result)
        {
            foreach (var taskSource in m_pendingRequests.Values)
            {
                taskSource.TrySetResult(result);
            }

            m_pendingRequests.Clear();
        }
    }
}
