// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Plugin.Grpc;
using BuildXL.Utilities;
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

        private static DateTime GetDeadline(int timeout)
        {
            return DateTime.UtcNow.Add(TimeSpan.FromMilliseconds(timeout));
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

        //private PluginMessage GetPluginMessageByType(PluginMessage.Types.MessageType messageType)
        //{
        //     return new PluginMessage() { MessageType = messageType };
        //}

        private static CallOptions GetCallOptions(string requestId)
        {
            var metaData = new Metadata();
            metaData.Add(GrpcPluginSettings.PluginReqeustId, requestId);
            return new CallOptions(deadline: GetDeadline(GrpcPluginSettings.RequestTimeoutInMiilliSeceonds)).WithWaitForReady(true).WithHeaders(metaData);
        }

        private async Task<PluginResponseResult<T>> HandleRpcExceptionWithCallAsync<T>(Func<Task<T>> asyncCall, string reqId)
        {
            uint numOfRetry = 0;
            Failure<string> failure = null;

            while (numOfRetry < MAX_RETRY && Channel.State != ChannelState.Shutdown)
            {
                try
                {
                    var response = await asyncCall.Invoke();
                    return new PluginResponseResult<T>(response, PluginResponseState.Succeeded, reqId, numOfRetry);
                }
                catch (RpcException e)
                {
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
                }
                catch (NotImplementedException e)
                {
                    Logger.Error($"plugin method is not implementated, this may be because unmatched plugin client is picked up, see details: {e.Message}");
                    return new PluginResponseResult<T>(PluginResponseState.Fatal, reqId, numOfRetry, failure);
                }
#pragma warning disable EPC12
                catch (Exception e)
                {
                    Logger.Error(e.Message);
                    failure = new Failure<string>(e.Message);
                }
#pragma warning restore EPC12
                finally
                {
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
                                                                                                         int exitCode)
        {
            var requestId = GetRequestId();
            var options = GetCallOptions(requestId);
            var request = new PluginMessage
            {
                ProcessResultMessage = new ProcessResultMessage
                {
                    Executable = executable,
                    Arguments = arguments,
                    ExitCode = exitCode,
                    StandardIn = input,
                    StandardOut = ouptut,
                    StandardErr = error,
                },
            };
            
            var response = await HandleRpcExceptionWithCallAsync(
                async () =>
                {
                    var response = await PluginServiceClient.ProcessResultAsync(request, options);
                    return response.ProcessResultMessageResponse;
                }, requestId);
            return response;
        }
    }
}
