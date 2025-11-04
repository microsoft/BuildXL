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

namespace BuildXL.Plugin
{
    /// <nodoc />
    public class PluginClient: IPluginClient, IDisposable
    {
        private static int s_requestId = 0;
        private bool m_channelRequestToShutdown = false;

        /// <nodoc />
        public Channel Channel { get; }

        /// <nodoc />
        public PluginServiceClient PluginServiceClient { get; }

        /// <nodoc />
        public ILogger Logger { get; }

        /// <nodoc />
        public bool IsConnected => Channel.State == ChannelState.Ready;
        /// <nodoc />
        public bool IsShutDown => Channel.State == ChannelState.Shutdown;

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
            Channel = new Channel(
                ipAddress,
                port,
                ChannelCredentials.Insecure,
                GrpcPluginSettings.GetChannelOptions());
            PluginServiceClient = new PluginServiceClient(Channel, Channel.Intercept(new PluginGrpcInterceptor(logger)));
            Logger = logger;
        }

        /// <nodoc />
        public Task ShutDown()
        {
            m_channelRequestToShutdown = true;
            // channel is shared by multiple clients so that only need to be disposed once
            return Channel.ShutdownAsync();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (!m_channelRequestToShutdown)
            {
                ShutDown().GetAwaiter().GetResult();
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
            uint numOfRetry = 0;
            Failure<string> failure = null;

            // Deadlines and retries: https://learn.microsoft.com/en-us/aspnet/core/grpc/deadlines-cancellation?view=aspnetcore-8.0#deadlines-and-retries
            while (numOfRetry < MAX_RETRY && Channel.State != ChannelState.Shutdown)
            {
                try
                {
                    Logger.Debug($"Sending request for requestId:{reqId} at {DateTime.UtcNow:HH:mm:ss.fff} (current active requests: {m_currentActiveRequestsCount})");
                    Interlocked.Increment(ref m_currentActiveRequestsCount);
                    var response = await asyncCall.Invoke();
                    Logger.Debug($"Received response for requestId:{reqId} at {DateTime.UtcNow:HH:mm:ss.fff}");
                    return new PluginResponseResult<T>(response, PluginResponseState.Succeeded, reqId, numOfRetry);
                }
                catch (RpcException e)
                {
                    Logger.Debug($"requestId:{reqId} has failed due to RpcException {e}");
                    failure = new Failure<string>(e.Message);
                    if (e.StatusCode == StatusCode.Cancelled)
                    {
                        Logger.Error(e.Message);
                        return new PluginResponseResult<T>(PluginResponseState.Cancelled, reqId, numOfRetry, failure);
                    }
                    else if(e.StatusCode == StatusCode.Unimplemented)
                    {
                        Logger.Error($"plugin method is not implementated, this may be because unmatched plugin client is picked up, see details: {e.Message}");
                        return new PluginResponseResult<T>(PluginResponseState.Fatal, reqId, numOfRetry, failure);
                    }
                    else if (e.StatusCode == StatusCode.DeadlineExceeded)
                    {
                        Logger.Error($"Deadline has been exceeded. Deadlines are global across all retries so retrying won't work");
                        return new PluginResponseResult<T>(PluginResponseState.Failed, reqId, numOfRetry, failure);
                    }
                }
                catch (NotImplementedException e)
                {
                    Logger.Debug($"requestId:{reqId} has failed due to NotImplementedException {e}");
                    Logger.Error($"plugin method is not implementated, this may be because unmatched plugin client is picked up, see details: {e.Message}");
                    return new PluginResponseResult<T>(PluginResponseState.Fatal, reqId, numOfRetry, failure);
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
                    numOfRetry++;
                }
            }

            return new PluginResponseResult<T>(PluginResponseState.Failed, reqId, numOfRetry, failure);
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
                async () => {
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
