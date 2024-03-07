// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
#pragma warning disable 0414

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Engine.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Core.Tasks;
using BuildXL.Utilities.Core.Tracing;
using BuildXL.Utilities.Tracing;
using Grpc.Core;
using static BuildXL.Engine.Distribution.RemoteWorker;
using BuildXL.Cache.ContentStore.Grpc;
using BuildXL.Distribution.Grpc;

#if NET6_0_OR_GREATER
using Grpc.Net.Client.Configuration;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
#endif

namespace BuildXL.Engine.Distribution.Grpc
{
    /// <nodoc/>
    internal sealed class ClientConnectionManager
    {
        public class ConnectionFailureEventArgs : EventArgs
        {
            /// <nodoc />
            public ConnectionFailureType Type { get; init; }

            /// <nodoc />
            public string Details { get; init; }

            /// <nodoc />
            public ConnectionFailureEventArgs(ConnectionFailureType failureType, string details)
            {
                Type = failureType;
                Details = details;
            }

            /// <summary>
            /// Log an appropriate message according to the type of connection failure
            /// </summary>
            public void Log(LoggingContext loggingContext, string machineName)
            {
                var details = Details ?? "";
                switch (Type)
                {
                    case ConnectionFailureType.CallDeadlineExceeded:
                    case ConnectionFailureType.ReconnectionTimeout:
                    case ConnectionFailureType.RemotePipTimeout:
                    case ConnectionFailureType.HeartbeatFailure:
                        Logger.Log.DistributionConnectionTimeout(loggingContext, machineName, details);
                        break;
                    case ConnectionFailureType.UnrecoverableFailure:
                        Logger.Log.DistributionConnectionUnrecoverableFailure(loggingContext, machineName, details);
                        break;
                    default:
                        Contract.Assert(false, "Unknown failure type");
                        break;
                }
            }
        }

        /// <summary>
        /// Default channel options for clients/servers to send/receive unlimited messages.
        /// </summary>
        private static readonly ChannelOption[] s_defaultChannelOptions = new ChannelOption[] { new ChannelOption(ChannelOptions.MaxSendMessageLength, int.MaxValue), new ChannelOption(ChannelOptions.MaxReceiveMessageLength, int.MaxValue) };

        public static readonly IEnumerable<ChannelOption> ServerChannelOptions = GetServerChannelOptions();

        private readonly LoggingContext m_loggingContext;
        private readonly DistributedInvocationId m_invocationId;
        private readonly Task m_monitorConnectionTask;
        public event EventHandler<ConnectionFailureEventArgs> OnConnectionFailureAsync;
        private volatile bool m_isShutdownInitiated;
        private volatile bool m_isExitCalledForServer;
        private readonly CancellationTokenSource m_exitTokenSource = new CancellationTokenSource();
        private volatile bool m_attached;
        private readonly string m_ipAddress;
        private readonly CounterCollection<DistributionCounter> m_counters;
        private readonly CancellableTimedAction m_heartbeatAction;
        private readonly Func<CallOptions, Task> m_heartbeatCall;
        private int m_numConsecutiveHeartbeatFails;
        private static string s_debugLogPathBase;

#if NET6_0_OR_GREATER
        internal readonly GrpcChannel Channel;
        private ConnectivityState State => Channel.State;

        private int m_numReconnectAttempts;
        private static LogLevel s_debugLogVerbosity;
#endif

        private string GenerateLog(string traceId, string status, uint numTry, string description)
        {
            // example: [MW1AAP45DD9145A] Call #1 e709c ExecutePips: 1 pips, 5 file hashes, 4F805AF2204AA5BA. 
            // example: [MW1AAP45DD9145A] Sent #1.e709c 
            // example: [MW1AAP45DD9145A] Fail #1 e709c Failure:
            string tryText = numTry != 1 ? numTry.ToString() : string.Empty;
            return $"{status}{tryText} {traceId} {description}";
        }

        public ClientConnectionManager(LoggingContext loggingContext, string ipAddress, int port, DistributedInvocationId invocationId, CounterCollection<DistributionCounter> counters, Func<CallOptions, Task> heartbeatCall)
        {
            m_invocationId = invocationId;
            m_loggingContext = loggingContext;
            m_ipAddress = ipAddress;
            m_counters = counters;

#if NET6_0_OR_GREATER
            Channel = SetupGrpcNetClient(ipAddress, port);
            m_monitorConnectionTask = MonitorConnectionAsync();

            Contract.Assert(Channel != null, "Channel must be initialized");
#else
            m_monitorConnectionTask = null;
#endif

            m_heartbeatCall = heartbeatCall;
            m_heartbeatAction = new CancellableTimedAction(SendHeartbeat, GrpcSettings.HeartbeatIntervalMs);
        }

        private void SendHeartbeat()
        {
            if (m_isExitCalledForServer)
            {
                return;
            }

            var result = CallAsync(m_heartbeatCall, "Heartbeat", m_exitTokenSource.Token, doNotRetry: true, timeout: TimeSpan.FromMinutes(1)).GetAwaiter().GetResult();

            if (result.Succeeded)
            {
                m_numConsecutiveHeartbeatFails = 0;
            }
            else
            {
                ++m_numConsecutiveHeartbeatFails;
                m_counters.IncrementCounter(DistributionCounter.ConnectionManagerFailedHeartbeats);

                if (m_numConsecutiveHeartbeatFails > 5)
                {
                    OnConnectionFailureAsync?.Invoke(this, new ConnectionFailureEventArgs(ConnectionFailureType.HeartbeatFailure, $"Heartbeats consecutively failed. Attempts: {m_numConsecutiveHeartbeatFails}"));
                }
            }
        }

#if NET6_0_OR_GREATER
        private GrpcChannel SetupGrpcNetClient(string ipAddress, int port)
        {
            var handler = new SocketsHttpHandler
            {
                UseCookies = false,
                ConnectTimeout = EngineEnvironmentSettings.WorkerAttachTimeout,
                Expect100ContinueTimeout = TimeSpan.Zero,
                PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
                EnableMultipleHttp2Connections = true
            };

            if (EngineEnvironmentSettings.GrpcKeepAliveEnabled)
            {
                handler.KeepAlivePingDelay = TimeSpan.FromSeconds(300); // 5m-frequent pings
                handler.KeepAlivePingTimeout = TimeSpan.FromSeconds(60); // wait for 1m to receive ack for the ping before closing connection.
                handler.KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always;
            }

            var channelOptions = new GrpcChannelOptions
            {
                MaxSendMessageSize = int.MaxValue,
                MaxReceiveMessageSize = int.MaxValue,
                HttpHandler = handler,
            };

            if (s_debugLogPathBase != null)
            {
                // Enable logging from the client
                channelOptions.LoggerFactory = LoggerFactory.Create(l =>
                {
                    l.AddProvider(new GrpcFileLoggerAdapter(s_debugLogPathBase + $".client.{ipAddress}_{port}.grpc"));
                    l.SetMinimumLevel(s_debugLogVerbosity);
                });
            }

            if (EngineEnvironmentSettings.GrpcDotNetServiceConfigEnabled)
            {
                var defaultMethodConfig = new MethodConfig
                {
                    Names = { MethodName.Default },
                    RetryPolicy = new RetryPolicy
                    {
                        MaxAttempts = GrpcSettings.MaxAttempts,
                        InitialBackoff = TimeSpan.FromSeconds(2),
                        MaxBackoff = TimeSpan.FromSeconds(10),
                        BackoffMultiplier = 1.5,
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
            }

            string address;

            if (GrpcSettings.EncryptionEnabled)
            {
                SetupChannelOptionsForEncryption(handler);
                address = $"https://{ipAddress}:{port}";
                Logger.Log.GrpcTrace(m_loggingContext, ipAddress, "Grpc.NET auth is enabled.");
            }
            else
            {
                AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
                address = $"http://{ipAddress}:{port}";
            }

            if (GrpcSettings.AuthenticationEnabled)
            {
                var credentials = GetCallCredentialsWithToken();
                channelOptions.Credentials = ChannelCredentials.Create(new SslCredentials(), credentials);
            }

            return GrpcChannel.ForAddress(address, channelOptions);
        }

        private void SetupChannelOptionsForEncryption(SocketsHttpHandler httpHandler)
        {
            string certSubjectName = GrpcSettings.CertificateSubjectName;

            X509Certificate2 certificate = null;

            try
            {
                certificate = GrpcEncryptionUtils.TryGetEncryptionCertificate(certSubjectName, GrpcSettings.CertificateStore, out string error);
            }
            catch (Exception e)
            {
                Logger.Log.GrpcTraceWarning(m_loggingContext, m_ipAddress, $"An exception occurred when finding a certificate: '{e}'.");
            }

            if (certificate == null)
            {
                Logger.Log.GrpcTraceWarning(m_loggingContext, m_ipAddress, $"No certificate found that matches subject name: '{certSubjectName}'.");
                return;
            }

            httpHandler.SslOptions.ClientCertificates = new X509CertificateCollection { certificate };

            string buildUserCertificateChainsPath = EngineEnvironmentSettings.CBBuildUserCertificateChainsPath.Value;

            httpHandler.SslOptions.RemoteCertificateValidationCallback =
                (requestMessage, certificate, chain, errors) =>
            {
                if (buildUserCertificateChainsPath != null)
                {
                    if (!GrpcEncryptionUtils.TryValidateCertificate(buildUserCertificateChainsPath, chain, out string errorMessage))
                    {
                        Logger.Log.GrpcTraceWarning(m_loggingContext, m_ipAddress, $"Certificate is not validated: '{errorMessage}'.");
                        return false;
                    }
                }

                // If the path for the chains is not provided, we will not validate the certificate.
                return true;
            };
        }
#endif

        // Verbose logging meant for debugging only
        internal static void EnableVerboseLogging(string path, GrpcEnvironmentOptions.GrpcVerbosity verbosity)
        {
            s_debugLogPathBase = path;

#if NET6_0_OR_GREATER
            // Adapt from GrpcEnvironmentOptions.GrpcVerbosity.
            // We are slightly more 'verbose' here (i.e. Debug => Trace and Error => Warning)
            // to account for the finer granularity and considering that the gRPC.NET client logging
            // is not as verbose as the Grpc.Core one.
            s_debugLogVerbosity = verbosity switch
            {
                GrpcEnvironmentOptions.GrpcVerbosity.Disabled => LogLevel.None,
                GrpcEnvironmentOptions.GrpcVerbosity.Debug => LogLevel.Trace,
                GrpcEnvironmentOptions.GrpcVerbosity.Info => LogLevel.Debug,
                GrpcEnvironmentOptions.GrpcVerbosity.Error => LogLevel.Warning,
                _ => LogLevel.Error
            };
#endif
        }

        private CallCredentials GetCallCredentialsWithToken()
        {
            string buildIdentityTokenLocation = EngineEnvironmentSettings.CBBuildIdentityTokenPath;

            string token = GrpcEncryptionUtils.TryGetTokenBuildIdentityToken(buildIdentityTokenLocation);

            if (token == null)
            {
                Logger.Log.GrpcTraceWarning(m_loggingContext, m_ipAddress, $"No token found in the following location: {buildIdentityTokenLocation}.");
                return null;
            }

            return CallCredentials.FromInterceptor((context, metadata) =>
            {
                if (!string.IsNullOrEmpty(token))
                {
                    metadata.Add("Authorization", token);
                }

                return Task.CompletedTask;
            });
        }

        private static IEnumerable<ChannelOption> GetServerChannelOptions()
        {
            List<ChannelOption> channelOptions = new List<ChannelOption>();
            channelOptions.AddRange(s_defaultChannelOptions);
            if (EngineEnvironmentSettings.GrpcKeepAliveEnabled)
            {
                // Pings are sent from client to server, and we do not want server to send pings to client due to the overhead concerns.
                // We just need to make server accept the pings.
                channelOptions.Add(new ChannelOption(ExtendedChannelOptions.KeepAlivePermitWithoutCalls, 1)); // enable receiving pings with no data
                channelOptions.Add(new ChannelOption(ExtendedChannelOptions.KeepAliveTimeMs, 300000)); // 5m-frequent pings
                channelOptions.Add(new ChannelOption(ExtendedChannelOptions.KeepAliveTimeoutMs, 60000)); // wait for 1m to receive ack for the ping before closing connection.
                channelOptions.Add(new ChannelOption(ExtendedChannelOptions.MaxPingsWithoutData, 0)); // no limit for pings with no header/data
                channelOptions.Add(new ChannelOption(ExtendedChannelOptions.MinSentPingIntervalWithoutDataMs, 300000)); // expecting 5m-frequent pings with no header/data. 
            }

            return channelOptions;
        }

#if NET6_0_OR_GREATER
        private async Task MonitorConnectionAsync()
        {
            await Task.Yield();

            ConnectivityState state = ConnectivityState.Idle;
            ConnectivityState lastState = state;

            var connectingStateTimer = new Stopwatch();

            while (state != ConnectivityState.Shutdown)
            {
                // We start monitoring for disconnections only after observing some 'connected' (i.e., Ready, Idle)
                // state to avoid abandoning workers that may become available some time after the build
                // starts in the orchestrator -- we will still timeout in this case but this will
                // be governed by WorkerAttachTimeout rather than DistributionConnectTimeout.
                bool monitorConnectingState = m_attached;

                try
                {
                    lastState = state;
                    await Channel.WaitForStateChangedAsync(state, m_exitTokenSource.Token);
                    state = State;  // Pick up the new state as soon as possible as it may change
                }
                catch (ObjectDisposedException)
                {
                    // The channel has been already shutdown and handle was disposed
                    // (https://github.com/grpc/grpc/blob/master/src/csharp/Grpc.Core/Channel.cs#L160)
                    // We shouldn't fail or leave this unobserved, instead we just stop monitoring
                    Logger.Log.GrpcTrace(m_loggingContext, m_ipAddress, $"{lastState} -> Disposed. Assuming shutdown was requested");
                    break;
                }
                catch (TaskCanceledException)
                {
                    Logger.Log.GrpcTrace(m_loggingContext, m_ipAddress, $"{lastState} -> TaskCancelledException. Assuming shutdown was requested");
                    break;
                }

                Logger.Log.GrpcTrace(m_loggingContext, m_ipAddress, $"{lastState} -> {state}");

                if (state == ConnectivityState.Ready && GrpcSettings.HeartbeatEnabled)
                {
                    // When connected to the server, start heartbeat messages.
                    m_heartbeatAction.Start();
                }

                // Check if we're stuck in reconnection attemps after losing connection
                // In this situation, the state will alternate between "Connecting" and "TransientFailure"
                if (state == ConnectivityState.Connecting || state == ConnectivityState.TransientFailure)
                {
                    if (monitorConnectingState && !connectingStateTimer.IsRunning)
                    {
                        connectingStateTimer.Start();
                    }
                }
                else
                {
                    connectingStateTimer.Reset();
                }

                if (monitorConnectingState && connectingStateTimer.IsRunning && connectingStateTimer.Elapsed >= EngineEnvironmentSettings.DistributionConnectTimeout)
                {
                    m_counters.IncrementCounter(DistributionCounter.ConnectionManagerTimeout);
                    OnConnectionFailureAsync?.Invoke(this, new ConnectionFailureEventArgs(ConnectionFailureType.ReconnectionTimeout, $"Timed out while the gRPC layer was trying to reconnect to the server. Timeout: {EngineEnvironmentSettings.DistributionConnectTimeout.Value.TotalMinutes} minutes"));
                    break;
                }

                // If we requested 'exit' for the server, the channel can go to 'Idle' state.
                // We should not reconnect to the channel again in that case.
                if (state == ConnectivityState.Idle && !m_isExitCalledForServer)
                {
                    m_counters.IncrementCounter(DistributionCounter.ConnectionManagerIdle);

                    if (!await TryReconnectAsync())
                    {
                        OnConnectionFailureAsync?.Invoke(this, new ConnectionFailureEventArgs(ConnectionFailureType.ReconnectionTimeout, $"Reconnection attempts from the Idle state failed. Reconnection attempts: {m_numReconnectAttempts}"));
                        break;
                    }
                }
            }
        }

        private async Task<bool> TryReconnectAsync()
        {
            if (m_numReconnectAttempts > 3)
            {
                // We sometimes go into deadlock between Idle and Ready states. 
                // As soon as we connect to the worker, the state is again back to Idle from Ready. 
                // To avoid the deadlock, we only allow three reconnection attempts.
                return false;
            }

            ++m_numReconnectAttempts;

            int numRetries = 0;
            bool connectionSucceeded = false;

            while (numRetries < GrpcSettings.MaxAttempts)
            {
                numRetries++;

                // Try connecting with timeout
                connectionSucceeded = await TryConnectChannelAsync(GrpcSettings.CallTimeout, nameof(TryReconnectAsync));
                if (connectionSucceeded)
                {
                    return true;
                }
                else if (IsNonRecoverableState(State))
                {
                    // If the end state is a non-recovarable state, there is no hope for the reconnection.
                    return false;
                }
            }

            // If the connection is not established after retries, return false.
            return false;
        }

        private static bool IsNonRecoverableState(ConnectivityState state)
        {
            switch (state)
            {
                case ConnectivityState.Idle:
                case ConnectivityState.Shutdown:
                    return true;
                default:
                    return false;
            }
        }

        private async Task<bool> TryConnectChannelAsync(TimeSpan timeout, string operation, StopwatchSlim? watch = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            watch = watch ?? StopwatchSlim.Start();
            try
            {
                Logger.Log.GrpcTrace(m_loggingContext, m_ipAddress, $"Connecting by {operation}");

                CancellationTokenSource source = new CancellationTokenSource();
                source.CancelAfter(timeout);

                using (CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, source.Token))
                {
                    await Channel.ConnectAsync(linkedCts.Token);
                }

                Logger.Log.GrpcTrace(m_loggingContext, m_ipAddress, $"Connected in {(long)watch.Value.Elapsed.TotalMilliseconds}ms");
            }
            catch (Exception e)
            {
#pragma warning disable EPC12 // Suspicious exception handling: only Message property is observed in exception block.
                Logger.Log.GrpcTrace(m_loggingContext, m_ipAddress, $"{State}. Failed to connect in {(long)watch.Value.Elapsed.TotalMilliseconds}ms. Failure {e.Message}");
#pragma warning restore EPC12 // Suspicious exception handling: only Message property is observed in exception block.

                return false;
            }

            return true;
        }
#else
        private Task<bool> TryConnectChannelAsync(TimeSpan timeout, string operation, StopwatchSlim? watch = null, CancellationToken cancellationToken = default(CancellationToken)) => Task.FromResult(false);
#endif

        /// <summary>
        /// Ready for exit.
        /// </summary>
        /// <remarks>
        /// If this is an exit operation, it will make the server to exit on the other machine.
        /// We need to be aware of this case as we do not want to reconnect to server.
        /// </remarks>
        public void ReadyForExit() => m_isExitCalledForServer = true;
       
        public async Task CloseAsync()
        {
            if (!m_isShutdownInitiated)
            {
                m_isShutdownInitiated = true;
                ReadyForExit();

                m_heartbeatAction.Cancel();
                m_heartbeatAction.Join();

#if NET6_0_OR_GREATER
                Channel.Dispose();
#endif

                // WaitForStateChangedAsync hangs when you dispose/shutdown the channel when it is 'idle'.
                // That's why, we pass a cancellation token to WaitForStateChangedAsync and cancel 
                await m_exitTokenSource.CancelTokenAsyncIfSupported();
            }

            if (m_monitorConnectionTask != null)
            {
                await m_monitorConnectionTask;
            }
        }

        public void OnAttachmentCompleted() => m_attached = true;

        public async Task<RpcCallResult<Unit>> CallAsync(
            Func<CallOptions, Task> func,
            string operation,
            CancellationToken cancellationToken = default(CancellationToken),
            bool waitForConnection = false,
            bool doNotRetry = false,
            TimeSpan? timeout = null)
        {
            var watch = StopwatchSlim.Start();

            TimeSpan waitForConnectionDuration = TimeSpan.Zero;
            TimeSpan totalCallDuration = TimeSpan.Zero;

            if (waitForConnection)
            {
                bool connectionSucceeded = await TryConnectChannelAsync(GrpcSettings.WorkerAttachTimeout, operation, watch, cancellationToken);
                waitForConnectionDuration = watch.Elapsed;

                if (!connectionSucceeded)
                {
                    return new RpcCallResult<Unit>(RpcCallResultState.Cancelled, attempts: 1, duration: TimeSpan.Zero, waitForConnectionDuration);
                }
            }

            var headerResult = GrpcUtils.InitializeHeaders(m_invocationId);
            string traceId = headerResult.traceId;

            RpcCallResultState state = RpcCallResultState.Succeeded;
            Failure failure = null;

            uint numTry = 0;
            var timeouts = 0;
            while (numTry < GrpcSettings.MaxAttempts)
            {
                numTry++;
                watch.ElapsedAndReset();

                try
                {
                    var callOptions = new CallOptions(
                        deadline: DateTime.UtcNow.Add(timeout ?? GrpcSettings.CallTimeout),
                        cancellationToken: cancellationToken,
                        headers: headerResult.headers).WithWaitForReady();

                    Logger.Log.GrpcTrace(m_loggingContext, m_ipAddress, GenerateLog(traceId, "Call", numTry, operation));
                    await func(callOptions);
                    Logger.Log.GrpcTrace(m_loggingContext, m_ipAddress, GenerateLog(traceId, "Sent", numTry, string.Empty));

                    state = RpcCallResultState.Succeeded;
                    break;
                }
                catch (RpcException e)
                {
                    Logger.Log.GrpcTrace(m_loggingContext, m_ipAddress, GenerateLog(traceId, "Fail", numTry, e.Message));
                    state = e.StatusCode == StatusCode.Cancelled ? RpcCallResultState.Cancelled : RpcCallResultState.Failed;
                    failure = state == RpcCallResultState.Failed ? new RecoverableExceptionFailure(new BuildXLException(e.Message)) : null;

                    m_counters.IncrementCounter(DistributionCounter.ConnectionManagerFailedCalls);

                    if (e.Status.StatusCode == StatusCode.DeadlineExceeded)
                    {
                        timeouts++;
                    }

                    if (e.Trailers.Get(GrpcMetadata.IsUnrecoverableError)?.Value == GrpcMetadata.True)
                    {
                        OnConnectionFailureAsync?.Invoke(this, new ConnectionFailureEventArgs(ConnectionFailureType.UnrecoverableFailure, e.Status.Detail));
                        state = RpcCallResultState.Failed;

                        // Unrecoverable failure - do not retry
                        break;
                    }

                    // If the call is cancelled or channel is shutdown, then do not retry the call.
                    if (state == RpcCallResultState.Cancelled || m_isShutdownInitiated)
                    {
                        break;
                    }

                    if (doNotRetry)
                    {
                        break;
                    }
                }
                catch (ObjectDisposedException e)
                {
                    state = RpcCallResultState.Failed;
                    failure = new RecoverableExceptionFailure(new BuildXLException(e.Message));
                    Logger.Log.GrpcTrace(m_loggingContext, m_ipAddress, GenerateLog(traceId, "Fail", numTry, e.Message));
                    m_counters.IncrementCounter(DistributionCounter.ConnectionManagerFailedCalls);

                    // If stream is already disposed, we cannot retry call. 
                    break;
                }
                finally
                {
                    totalCallDuration += watch.Elapsed;
                }
            }

            if (state == RpcCallResultState.Succeeded)
            {
                return new RpcCallResult<Unit>(Unit.Void, attempts: numTry, duration: totalCallDuration, waitForConnectionDuration: waitForConnectionDuration);
            }
            else if (m_attached && timeouts == GrpcSettings.MaxAttempts)
            {
                // We assume the worker is lost if we timed out every time
                OnConnectionFailureAsync?.Invoke(this,
                    new ConnectionFailureEventArgs(ConnectionFailureType.CallDeadlineExceeded,
                    $"Timed out on a call to the worker. Assuming the worker is dead. Call timeout: {GrpcSettings.CallTimeout.TotalMinutes} min. Retries: {GrpcSettings.MaxAttempts}"));
            }

            return new RpcCallResult<Unit>(
                state,
                attempts: numTry,
                duration: totalCallDuration,
                waitForConnectionDuration: waitForConnectionDuration,
                lastFailure: failure);
        }

        public Task<RpcCallResult<Unit>> FinalizeStreamAsync<TRequest>(AsyncClientStreamingCall<TRequest, RpcResponse> stream) => CallAsync(
                    async (_) =>
                    {
                        await stream.RequestStream.CompleteAsync();
                        await stream;
                        stream.Dispose();
                    },
                    nameof(FinalizeStreamAsync),
                    doNotRetry: true);
    }
}