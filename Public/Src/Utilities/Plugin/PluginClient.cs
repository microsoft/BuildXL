// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Plugin.Grpc;
using BuildXL.Utilities.Core;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Core.Logging;

#if NET6_0_OR_GREATER
using System.Net.Http;
using Grpc.Net.Client;
using Grpc.Net.Client.Configuration;
#endif

namespace BuildXL.Plugin
{
    /// <nodoc />
    public class PluginClient : IPluginClient, IDisposable
    {
        private static int s_requestId = 0;
        private bool m_channelRequestToShutdown = false;


        /// <nodoc />
        public PluginServiceClient PluginServiceClient { get; }

        /// <nodoc />
        public ILogger Logger { get; }

#if NET6_0_OR_GREATER
        /// <nodoc />
        public GrpcChannel Channel { get; }
#endif

        /// <nodoc />
        private int m_overrideRequestTimeout;
        /// <inheritdoc />
        public int RequestTimeout
        {
            get
            {
                return m_overrideRequestTimeout > 0 ? m_overrideRequestTimeout : GrpcPluginSettings.RequestTimeoutInMilliSeconds;
            }
            set
            {
                m_overrideRequestTimeout = value;
            }
        }

        /// <inheritdoc />
        public HashSet<string> SupportedProcesses { get; set; }

        /// <inheritdoc />
        public bool ExitGracefully { get; set; } = true; // On by default

        /// <nodoc />
        public const int MAX_RETRY = 5;
        /// <nodoc />
        public PluginClient(string ipAddress, int port, ILogger logger = null)
        {
#if NET6_0_OR_GREATER
            Channel = CreateGrpcChannel(ipAddress, port);
            PluginServiceClient = new PluginServiceClient(Channel, Channel.Intercept(new PluginGrpcInterceptor(logger)));
#endif
            Logger = logger;
        }

#if NET6_0_OR_GREATER
        private GrpcChannel CreateGrpcChannel(string ipAddress, int port)
        {
            var handler = new SocketsHttpHandler
            {
                UseCookies = false,
                ConnectTimeout = TimeSpan.FromMilliseconds(GrpcPluginSettings.ConnectionTimeoutInMilliSeconds),
                Expect100ContinueTimeout = TimeSpan.Zero,
                PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
                EnableMultipleHttp2Connections = true
            };

            var channelOptions = new GrpcChannelOptions
            {
                MaxSendMessageSize = int.MaxValue,
                MaxReceiveMessageSize = int.MaxValue,
                MaxRetryBufferPerCallSize = null, // No limit on retry buffer size
                MaxRetryBufferSize = null, // No limit on retry buffer size
                HttpHandler = handler,
            };

            var defaultMethodConfig = new MethodConfig
            {
                Names = { MethodName.Default },
                RetryPolicy = new RetryPolicy
                {
                    MaxAttempts = GrpcPluginSettings.MaxAttempts,
                    InitialBackoff = TimeSpan.FromSeconds(0.5),
                    MaxBackoff = TimeSpan.FromSeconds(1),
                    BackoffMultiplier = 1.1,
                    RetryableStatusCodes = {
                            StatusCode.Unavailable,
                            StatusCode.Internal,
                            StatusCode.Unknown }
                }
            };

            channelOptions.ServiceConfig = new ServiceConfig
            {
                MethodConfigs = { defaultMethodConfig },
                LoadBalancingConfigs = { new PickFirstConfig() },
            };

            string address = $"http://{ipAddress}:{port}";

            return GrpcChannel.ForAddress(address, channelOptions);
        }
#endif

        /// <inheritdoc />
        public void Dispose()
        {
            if (!m_channelRequestToShutdown)
            {
#if NET6_0_OR_GREATER
                // channel is shared by multiple clients so that only need to be disposed once
                Channel.Dispose();
#endif
            }
        }

        private static string GetRequestId() => Interlocked.Increment(ref s_requestId).ToString();

        private CallOptions GetCallOptions(string requestId)
        {
            return new CallOptions(deadline: DateTime.UtcNow.AddMilliseconds(RequestTimeout))
            .WithWaitForReady(true).WithHeaders(new Metadata
            {
                { GrpcPluginSettings.PluginRequestId, requestId },
            });
        }

        private int m_currentActiveRequestsCount = 0;

        private async Task<PluginResponseResult<T>> HandleRpcExceptionWithCallAsync<T>(Func<Task<T>> asyncCall, string reqId)
        {
            Failure<string> failure = null;

            try
            {
                Logger.Debug($"Sending request for requestId:{reqId} at {DateTime.UtcNow:HH:mm:ss.fff} (current active requests: {m_currentActiveRequestsCount})");
                Interlocked.Increment(ref m_currentActiveRequestsCount);
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(RequestTimeout));
                var response = await asyncCall.Invoke();
                Logger.Debug($"Received response for requestId:{reqId} at {DateTime.UtcNow:HH:mm:ss.fff}");
                return new PluginResponseResult<T>(response, PluginResponseState.Succeeded, reqId);
            }
            catch (RpcException e)
            {
                Logger.Debug($"requestId:{reqId} has failed due to RpcException {e}");
                failure = new Failure<string>(e.Message);
                if (e.StatusCode == StatusCode.Cancelled)
                {
                    Logger.Error(e.Message);
                    return new PluginResponseResult<T>(PluginResponseState.Cancelled, reqId, failure);
                }
                else if (e.StatusCode == StatusCode.Unimplemented)
                {
                    Logger.Error($"plugin method is not implementated, this may be because unmatched plugin client is picked up, see details: {e.Message}");
                    return new PluginResponseResult<T>(PluginResponseState.Fatal, reqId, failure);
                }
                else if (e.StatusCode == StatusCode.DeadlineExceeded)
                {
                    Logger.Error($"Deadline has been exceeded. Deadlines are global across all retries so retrying won't work");
                    return new PluginResponseResult<T>(PluginResponseState.Failed, reqId, failure);
                }
            }
            catch (NotImplementedException e)
            {
                Logger.Debug($"requestId:{reqId} has failed due to NotImplementedException {e}");
                Logger.Error($"plugin method is not implementated, this may be because unmatched plugin client is picked up, see details: {e.Message}");
                return new PluginResponseResult<T>(PluginResponseState.Fatal, reqId, failure);
            }
#pragma warning disable EPC12
            catch (Exception e)
            {
                Logger.Debug($"requestId:{reqId} has failed due to Exception {e}");
                Logger.Error(e.Message);
                failure = new Failure<string>(e.Message);
            }
#pragma warning restore EPC12
            finally
            {
                Interlocked.Decrement(ref m_currentActiveRequestsCount);
            }

            return new PluginResponseResult<T>(PluginResponseState.Failed, reqId, failure);
        }

        /// <nodoc />
        public virtual async Task<PluginResponseResult<bool>> StartAsync()
        {
            var requestId = GetRequestId();
            var options = GetCallOptions(requestId);
            var request = new PluginMessage();
            var response = await HandleRpcExceptionWithCallAsync(
                async () =>
                {
                    var response = await PluginServiceClient.StartAsync(request, options);
                    return response.Status;
                }, requestId);
            return response;
        }

        /// <nodoc />
        public virtual async Task<PluginResponseResult<bool>> StopAsync()
        {
            var requestId = GetRequestId();
            var options = GetCallOptions(requestId);
            var request = new PluginMessage();
            var response = await HandleRpcExceptionWithCallAsync(
                async () =>
                {
                    var response = await PluginServiceClient.StopAsync(request, options);
                    return response.Status;
                }, requestId);
            return response;
        }

        /// <nodoc />
        public virtual async Task<PluginResponseResult<List<PluginMessageType>>> GetSupportedPluginMessageType()
        {
            var requestId = GetRequestId();
            var options = GetCallOptions(requestId);
            var request = new PluginMessage();
            var response = await HandleRpcExceptionWithCallAsync(
                async () =>
                {
                    var response = await PluginServiceClient.SupportedOperationAsync(request, options);

                    Interlocked.Exchange(ref m_overrideRequestTimeout, response.SupportedOperationResponse.Timeout);
                    var supportedProcesses = new HashSet<string>(response.SupportedOperationResponse.SupportedProcesses, StringComparer.OrdinalIgnoreCase);
                    SupportedProcesses = supportedProcesses;

                    return response.SupportedOperationResponse.Operation.Select(op => PluginMessageTypeHelper.ToPluginMessageType(op)).ToList();
                }, requestId);
            return response;
        }

        private PluginMessage GetLogParsePluginMessage(string message, bool isError)
        {
            var pluginMessage = new PluginMessage();
            var logType = isError ? LogType.Error : LogType.StandardOutput;
            pluginMessage.LogParseMessage = new LogParseMessage()
            {
                LogType = logType,
                Message = message,
            };
            return pluginMessage;
        }

        /// <nodoc />
        public virtual async Task<PluginResponseResult<LogParseResult>> ParseLogAsync(string message, bool isError)
        {
            var requestId = GetRequestId();
            var options = GetCallOptions(requestId);
            var request = GetLogParsePluginMessage(message, isError);
            var response = await HandleRpcExceptionWithCallAsync(
                async () =>
                {
                    var response = await PluginServiceClient.ParseLogAsync(request, options);
                    return response.LogParseMessageResponse.LogParseResult;
                }, requestId);
            return response;
        }

        /// <nodoc />
        public virtual async Task<PluginResponseResult<ProcessResultMessageResponse>> ProcessResultAsync(string executable,
                                                                                                         string arguments,
                                                                                                         ProcessStream input,
                                                                                                         ProcessStream ouptut,
                                                                                                         ProcessStream error,
                                                                                                         int exitCode,
                                                                                                         string pipSemiStableHash)
        {
            ProcessResultMessage message = new ProcessResultMessage
            {
                Executable = executable,
                Arguments = arguments,
                ExitCode = exitCode,
                StandardOut = ouptut,
                StandardErr = error,
                PipSemiStableHash = pipSemiStableHash,
            };

            if (input != null)
            {
                message.StandardIn = input;
            }

            var requestId = GetRequestId();
            var options = GetCallOptions(requestId);
            var request = new PluginMessage
            {
                ProcessResultMessage = message,
            };

            var response = await HandleRpcExceptionWithCallAsync(
                async () =>
                {
                    Logger.Debug($"Deadline for requestId:{requestId} is {options.Deadline:HH:mm:ss.fff}");
                    var response = await PluginServiceClient.ProcessResultAsync(request, options);
                    return response.ProcessResultMessageResponse;
                }, requestId);
            return response;
        }
    }
}
