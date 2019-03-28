// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if !DISABLE_FEATURE_BOND_RPC

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Engine.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using Microsoft.Bond;
using BuildXL.Utilities.Configuration;
using Void = Microsoft.Bond.Void;

namespace BuildXL.Engine.Distribution.InternalBond
{
    /// <summary>
    /// Manages connection, calls, and retries for a bond proxy. This class requires that the proxy/service expose a heartbeat. Calls are only retried
    /// if a successful heartbeat occurs. If heartbeat is unsuccessful for a certain period, the connection will time out and all calls will fail.
    /// </summary>
    /// <typeparam name="TProxy">the type of the proxy</typeparam>
    internal sealed class BondProxyConnectionManager<TProxy> : IDisposable
        where TProxy : class, IBondProxyWithHeartbeat
    {
        private const int DefaultMaxRetryCount = 100;

        /// <summary>
        /// The time to allow failed heartbeats before timing out
        /// </summary>
        public static TimeSpan InactivityTimeout => EngineEnvironmentSettings.DistributionInactiveTimeout;

        /// <summary>
        /// Maximum amount of time for failed connection before it is recreated
        /// </summary>
        private static TimeSpan ConnectionRefreshTimeout => EngineEnvironmentSettings.DistributionConnectTimeout;

        // Initialized with default values
        private int m_takeIndex;
        private bool m_exceededInactivityTimeout;
        private bool m_isShuttingDown;

        private readonly TimeSpan m_heartbeatInterval = TimeSpan.FromSeconds(15);
        private readonly object m_syncLock = new object();

        private readonly NullSemaphore m_proxySemaphore;
        private readonly Stopwatch m_stopwatch;
        private readonly Timer m_heartbeatTimer;
        private readonly TrackedConnection[] m_connections;

        // Mutable state
        private TaskSourceSlim<bool> m_isActiveCompletionSource;
        private DateTime m_lastSuccessfulHeartbeat;
        private IBondProxyLogger m_proxyLogger;

        // Initialized from constructor arguments
        private readonly BondTcpClient<TProxy> m_client;
        private readonly BondTcpClient<TProxy>.CreateProxyCallback m_createProxyCallback;
        private readonly LoggingContext m_loggingContext;
        private readonly int m_maxConnectionConcurrency;
        private readonly ConcurrentDictionary<string, int> m_outstandingCalls = new ConcurrentDictionary<string, int>();
        private readonly BufferManager m_bufferManager;
        private readonly DistributionServices m_services;
        private readonly CancellationTokenSource m_cancellationTokenSource = new CancellationTokenSource();

        public event EventHandler OnActivateConnection;

        public event EventHandler OnDeactivateConnection;

        public event EventHandler OnConnectionTimeOut;

        private string m_serverName;
        private string m_thisMachineId;
        private string m_thisMachineName = Environment.MachineName;

        /// <summary>
        /// The server name
        /// </summary>
        public string Server { get; private set; }

        /// <summary>
        /// The port
        /// </summary>
        public int Port { get; private set; }

        /// <summary>
        /// Class constructor
        /// </summary>
        /// <param name="client">the bond tcp client used to create the proxy</param>
        /// <param name="createProxyCallback">callback used to create a bond proxy</param>
        /// <param name="loggingContext">the logging context</param>
        /// <param name="services">shared services for distribution</param>
        /// <param name="maxConnectionConcurrency">the maximum number of connections</param>
        public BondProxyConnectionManager(
            BondTcpClient<TProxy> client,
            BondTcpClient<TProxy>.CreateProxyCallback createProxyCallback,
            LoggingContext loggingContext,
            DistributionServices services,
            int maxConnectionConcurrency = 1)
        {
            m_client = client;
            m_createProxyCallback = createProxyCallback;
            m_loggingContext = loggingContext;
            m_maxConnectionConcurrency = maxConnectionConcurrency;
            m_services = services;
            m_bufferManager = services.BufferManager;

            m_proxySemaphore = new NullSemaphore();

            // m_proxySemaphore = new SemaphoreSlim(m_maxConnectionConcurrency);
            m_stopwatch = Stopwatch.StartNew();
            m_heartbeatTimer = new Timer(HeartbeatTimerCallback);
            m_connections = new TrackedConnection[m_maxConnectionConcurrency];
            for (int i = 0; i < m_maxConnectionConcurrency; i++)
            {
                m_connections[i] = new TrackedConnection(this);
            }

            m_isActiveCompletionSource = TaskSourceSlim.Create<bool>();
        }

        /// <summary>
        /// Placeholder for semaphore in case call concurrency needs to be limited
        /// </summary>
        private sealed class NullSemaphore : IDisposable
        {
            private readonly Task<IDisposable> m_disposableTask = Task.FromResult<IDisposable>(System.IO.Stream.Null);

            [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic")]
            public Task WaitAsync(CancellationToken cancellationToken)
            {
                Analysis.IgnoreArgument(cancellationToken);
                return Unit.VoidTask;
            }

            public Task<IDisposable> AcquireAsync()
            {
                return m_disposableTask;
            }

            [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic")]
            public void Release()
            {
            }

            public void Dispose()
            {
            }
        }

        /// <summary>
        /// Starts the manager initiating the first heartbeat.
        /// </summary>
        /// <param name="server">the server</param>
        /// <param name="port">the port</param>
        /// <param name="proxyLogger">the logger</param>
        public void Start(string server, int port, IBondProxyLogger proxyLogger)
        {
            Server = server;
            Port = port;
            m_serverName = string.Format(CultureInfo.InvariantCulture, "{0}::{1}", Server, Port);
            m_lastSuccessfulHeartbeat = DateTime.UtcNow;
            m_proxyLogger = proxyLogger;
            var startTracker = CreateLoggingCallTracker(nameof(Start));
            m_thisMachineId = startTracker.CallId.ToString().Substring(0, 8);

            // Start the heartbeat timer
            m_heartbeatTimer.Change(dueTime: 0, period: Timeout.Infinite);
        }

        /// <summary>
        /// Terminates all active calls in the proxy
        /// </summary>
        public void Terminate()
        {
            m_cancellationTokenSource.Cancel();
            m_isShuttingDown = true;
        }

        private Task<BondTcpConnection<TProxy>> CreateConnectionAsync()
        {
            return m_client.ConnectAsync(Server, Port, m_createProxyCallback);
        }

        /// <summary>
        /// Heartbeat timer callback which initiates heartbeat calls to check that bond service is alive.
        /// </summary>
        private async void HeartbeatTimerCallback(object state)
        {
            var heartbeatCallTracker = CreateLoggingCallTracker("Heartbeat");
            if (m_isShuttingDown)
            {
                heartbeatCallTracker.OnStateChanged(BondCallState.HeartbeatTimerShutdown);
                return;
            }

            heartbeatCallTracker.OnStateChanged(BondCallState.HeartbeatBeforeCall);
            var heartbeatResult = await HeartbeatAsync(heartbeatCallTracker);
            heartbeatCallTracker.OnStateChanged(BondCallState.HeartbeatAfterCall);

            if (heartbeatResult.State == RpcCallResultState.Succeeded)
            {
                heartbeatCallTracker.OnStateChanged(BondCallState.HeartbeatSuccess);

                // TODO: Should any successful call reactivate the connection?
                // Successful heartbeat
                // Activate the connection if necessary
                m_lastSuccessfulHeartbeat = DateTime.UtcNow;
                OnActivateConnection?.Invoke(this, EventArgs.Empty);

                lock (m_syncLock)
                {
                    if (!m_isActiveCompletionSource.Task.IsCompleted)
                    {
                        m_isActiveCompletionSource.TrySetResult(true);
                    }
                }

                heartbeatCallTracker.OnStateChanged(BondCallState.HeartbeatAfterActivateConnection);
            }
            else
            {
                TimeSpan timeSinceLastSuccessfulHeartbeat = DateTime.UtcNow - m_lastSuccessfulHeartbeat;

                // Check if proxy has timed out
                if (timeSinceLastSuccessfulHeartbeat > InactivityTimeout)
                {
                    heartbeatCallTracker.OnStateChanged(BondCallState.HeartbeatTimerInactive);

                    m_exceededInactivityTimeout = true;
                    DeactivateConnection(null);
                    Logger.Log.DistributionDisableServiceProxyInactive(
                        m_loggingContext,
                        Server,
                        Port,
                        timeSinceLastSuccessfulHeartbeat.ToString());

                    OnConnectionTimeOut?.Invoke(this, EventArgs.Empty);

                    // Return without setting new timer due time to stop the timer
                    // since the proxy has timed out
                    return;
                }
            }

            lock (m_syncLock)
            {
                if (!m_isShuttingDown)
                {
                    heartbeatCallTracker.OnStateChanged(BondCallState.HeartbeatQueueTimer);
                    m_heartbeatTimer.Change(dueTime: m_heartbeatInterval, period: Timeout.InfiniteTimeSpan);
                }
                else
                {
                    heartbeatCallTracker.OnStateChanged(BondCallState.HeartbeatDeactivateTimer);
                }
            }
        }

        private Task<RpcCallResult<Void>> HeartbeatAsync(BondCallTracker heartbeatCallTracker, CancellationToken cancellationToken = default(CancellationToken))
        {
            var input = new RpcMessageBase();
            return Call(
                callAsync: (connection, callTracker) =>
                    CreateTaskForProxyCall<RpcMessageBase, Void>(
                        connection,
                        input,
                        callTracker: callTracker,
                        cancellationToken: cancellationToken),
                cancellationToken: cancellationToken,
                functionName: "Heartbeat",

                // Heartbeats should not wait for the connection to become active because the
                // heartbeat is what determines the active state
                allowInactive: true,
                callTracker: heartbeatCallTracker,

                // Only try once in the loop. The retry is implemented via the timer rather than in the call.
                maxTryCount: 1);
        }

        /// <summary>
        /// Creates a task for a bond proxy method call
        /// </summary>
        /// <typeparam name="TInput">the input type</typeparam>
        /// <typeparam name="T">the return type</typeparam>
        /// <param name="connection">the connection containing the active bond proxy</param>
        /// <param name="input">the input value</param>
        /// <param name="callTracker">the call tracker containing data about the call state</param>
        /// <param name="cancellationToken">cancellation token used to cancel the call</param>
        /// <returns>a task representing the result of the call</returns>
        private async Task<T> CreateTaskForProxyCall<TInput, T>(
            TrackedConnection connection,
            TInput input,
            BondCallTracker callTracker,
            CancellationToken cancellationToken = default(CancellationToken))
            where TInput : RpcMessageBase, IBondSerializable, new()
            where T : IBondSerializable, new()
        {
            using (var bufferProvider = m_bufferManager.GetBufferProvider())
            {
                IBondAdaptable adaptable = input as IBondAdaptable;
                if (adaptable != null)
                {
                    callTracker.OnStateChanged(BondCallState.Converting);
                    adaptable.Adapt(bufferProvider);
                    callTracker.OnStateChanged(BondCallState.Converted);
                }

                // Create a cancellation token source which can be disposed after call completes.
                // Need to unregister cancellation when call completes but this is difficult to do with
                // the CancellationTokenRegistration returned by CancellationToken.Register since it
                // is created inside the begin method delegate
                using (var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    input.SenderName = m_thisMachineName;
                    input.SenderId = m_thisMachineId;
                    input.BuildId = m_services.BuildId;

                    var message = new Message<TInput>(input);

                    // Assign checksum so messages can be verified on receipt
                    m_services.AssignChecksum(message);
                    message.Context.PacketHeaders.m_nettrace.m_callID.SetFromSystemGuid(callTracker.CallId);
                    Func<AsyncCallback, object, IAsyncResult> beginMethod = (callback, state) =>
                    {
                        var asyncResult = connection.Proxy.BeginRequest(callTracker.FunctionName, message, callback, bufferProvider.Allocator);
                        callTracker.OnStateChanged(BondCallState.InitiatedRequest);
                        cancellationTokenSource.Token.Register(() => connection.Proxy.CancelRequest(callTracker.FunctionName, asyncResult), useSynchronizationContext: false);
                        return asyncResult;
                    };

                    // Use the overload which takes an async callback to circumvent need to wait on wait handle in thread pool
                    var resultMessage = await Task.Factory.FromAsync(beginMethod, asyncResult => connection.Proxy.EndRequest<T>(callTracker.FunctionName, asyncResult), state: null);
                    return resultMessage.Payload.Value;
                }
            }
        }

        /// <summary>
        /// Calls a given function using the same retry logic used for bond calls
        /// </summary>
        /// <typeparam name="T">the result value type</typeparam>
        /// <param name="functionName">the function name for the call</param>
        /// <param name="callAsync">the delegate which initiates the call</param>
        /// <param name="cancellationToken">the cancellation token to cancel the call</param>
        /// <param name="shouldRetry">delegate to check result and indicate if it should retry the call</param>
        /// <returns>a task representing the result of the call</returns>
        public Task<RpcCallResult<T>> Call<T>(
            string functionName,
            Func<Task<T>> callAsync,
            CancellationToken cancellationToken = default(CancellationToken),
            Func<T, bool> shouldRetry = null)
        {
            return Call(
                callAsync: (connection, callTracker) => callAsync(),
                cancellationToken: cancellationToken,
                functionName: functionName,
                shouldRetry: shouldRetry,
                callTracker: null);
        }

        /// <summary>
        /// Creates a task for a bond proxy method call
        /// </summary>
        /// <typeparam name="TInput">the input type</typeparam>
        /// <typeparam name="T">the return type</typeparam>
        /// <param name="input">the input value</param>
        /// <param name="cancellationToken">cancellation token used to cancel the call</param>
        /// <param name="functionName">the function name of the call</param>
        /// <param name="description">the description text for the call</param>
        /// <param name="maxTryCount">the nubmer of times to try the call (0 to use default)</param>
        /// <returns>a task representing the result of the call</returns>
        public Task<RpcCallResult<T>> Call<TInput, T>(
            TInput input,
            CancellationToken cancellationToken = default(CancellationToken),
            [CallerMemberName] string functionName = null,
            string description = null,
            uint maxTryCount = 0)
            where TInput : RpcMessageBase, IBondSerializable, new()
            where T : IBondSerializable, new()
        {
            return Call(
                callAsync: (connection, callTracker0) =>
                    CreateTaskForProxyCall<TInput, T>(connection, input, callTracker0, cancellationToken),
                cancellationToken: cancellationToken,
                functionName: functionName,
                callTracker: CreateLoggingCallTracker(functionName, description),
                maxTryCount: maxTryCount);
        }

        /// <summary>
        /// Creates a task for a bond proxy method call
        /// </summary>
        /// <typeparam name="TInput">the input type</typeparam>
        /// <typeparam name="T">the return type</typeparam>
        /// <param name="input">the input value</param>
        /// <param name="callTracker">the call tracker containing data about the call state</param>
        /// <param name="cancellationToken">cancellation token used to cancel the call</param>
        /// <param name="maxTryCount">the maximum number of times to try the call (0 to use default)</param>
        /// <returns>a task representing the result of the call</returns>
        public Task<RpcCallResult<T>> Call<TInput, T>(
            TInput input,
            BondCallTracker callTracker,
            CancellationToken cancellationToken = default(CancellationToken),
            uint maxTryCount = 0)
            where TInput : RpcMessageBase, IBondSerializable, new()
            where T : IBondSerializable, new()
        {
            return Call(
                callAsync: (connection, callTracker0) =>
                    CreateTaskForProxyCall<TInput, T>(connection, input, callTracker0, cancellationToken),
                cancellationToken: cancellationToken,
                functionName: callTracker.FunctionName,
                callTracker: callTracker,
                maxTryCount: maxTryCount);
        }

        public LoggingBondCallTracker CreateLoggingCallTracker([CallerMemberName] string functionName = null, string description = null)
        {
            var callTracker = new LoggingBondCallTracker(m_loggingContext, new RpcMachineData() { MachineName = m_serverName }, description);
            callTracker.Initialize(m_stopwatch, functionName, Guid.NewGuid());
            return callTracker;
        }

        private TimeSpan GetElapsed(TimeSpan startTime)
        {
            return m_stopwatch.Elapsed - startTime;
        }

        private async Task<RpcCallResult<T>> Call<T>(
            Func<TrackedConnection, BondCallTracker, Task<T>> callAsync,
            CancellationToken cancellationToken,
            string functionName,
            BondCallTracker callTracker,
            bool allowInactive = false,
            Func<T, bool> shouldRetry = null,
            uint maxTryCount = 0)
        {
            using (var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_cancellationTokenSource.Token))
            {
                return await CallCore(callAsync, cancellationTokenSource.Token, functionName, callTracker, allowInactive, shouldRetry, maxTryCount);
            }
        }

        private async Task<RpcCallResult<T>> CallCore<T>(
            Func<TrackedConnection, BondCallTracker, Task<T>> callAsync,
            CancellationToken cancellationToken,
            string functionName,
            BondCallTracker callTracker,
            bool allowInactive = false,
            Func<T, bool> shouldRetry = null,
            uint maxTryCount = 0)
        {
            Contract.Requires(functionName != null);
            callTracker = callTracker ?? CreateLoggingCallTracker(functionName);

            TimeSpan waitForConnectionDuration = default(TimeSpan);
            Failure lastFailure = null;
            m_outstandingCalls.AddOrUpdate(functionName, 1, (k, i) => i + 1);

            // For heartbeat only try once
            if (maxTryCount == 0)
            {
                maxTryCount = DefaultMaxRetryCount;
            }

            for (uint retryCount = 0; retryCount < maxTryCount; retryCount++)
            {
                callTracker.TryCount = retryCount;

                if (retryCount != 0)
                {
                    // For retries, log a call start with the call tracker's updated retry count
                    callTracker.OnStateChanged(BondCallState.Started);
                    // Yield after first iteration to ensure
                    // we don't overflow the stack with async continuations
                    await Task.Yield();
                }

                TrackedConnection connection = null;
                try
                {
                    var startWaitForConnection = m_stopwatch.Elapsed;
                    callTracker.OnStateChanged(BondCallState.WaitingForConnection);

                    // Wait for a connection to become active via the a successful heartbeat
                    using (var connectionScope = await WaitForConnectionAsync(callTracker, allowInactive, cancellationToken))
                    {
                        // Log wait for connection success
                        var iterationWaitForConnectionDuration = GetElapsed(startWaitForConnection);
                        waitForConnectionDuration += iterationWaitForConnectionDuration;
                        callTracker.OnStateChanged(BondCallState.CompletedWaitForConnection);

                        // connection is not returned in the case that the proxy is shutting down or timed out
                        // other case is that this is a failed heartbeat call. In which case, just continue.
                        if (connectionScope.Connection == null)
                        {
                            if (m_isShuttingDown || m_exceededInactivityTimeout)
                            {
                                // Log the failure
                                lastFailure = new RecoverableExceptionFailure(new BuildXLException(m_isShuttingDown ?
                                    "Bond RPC Call failure: Proxy is shutting down" :
                                    "Bond RPC Call failure: Proxy timed out"));

                                callTracker.LogMessage("Could not retrieve connection. Failure={0}", lastFailure.DescribeIncludingInnerFailures());
                                callTracker.OnStateChanged(BondCallState.Failed);
                                return new RpcCallResult<T>(RpcCallResultState.Failed, retryCount + 1, callTracker.TotalDuration, waitForConnectionDuration, lastFailure);
                            }

                            continue;
                        }

                        connection = connectionScope.Connection;

                        // Make the actual call
                        var result = await callAsync(connection, callTracker);

                        // Check if call should be retried
                        if (shouldRetry != null && shouldRetry(result))
                        {
                            continue;
                        }

                        // Log the call completion
                        callTracker.OnStateChanged(BondCallState.Succeeded);
                        m_proxyLogger.LogSuccessfulCall(m_loggingContext, functionName, retryCount);
                        connectionScope.MarkSucceeded();
                        m_services.Counters.AddToCounter(DistributionCounter.SendPipBuildRequestCallDurationMs, (long)callTracker.TotalDuration.TotalMilliseconds);
                        return new RpcCallResult<T>(result, retryCount + 1, callTracker.TotalDuration, waitForConnectionDuration);
                    }
                }
                catch (OperationCanceledException)
                {
                    callTracker.OnStateChanged(BondCallState.Canceled);
                    return new RpcCallResult<T>(RpcCallResultState.Cancelled, retryCount + 1, callTracker.TotalDuration, waitForConnectionDuration);
                }
                catch (Exception ex)
                {
                    // If shutting down just return the failed result
                    if (ex is ObjectDisposedException && m_isShuttingDown)
                    {
                        lastFailure = new RecoverableExceptionFailure(new BuildXLException("Bond RPC Call failure: Proxy is shutting down", ex));
                        callTracker.LogMessage("{0}", lastFailure.DescribeIncludingInnerFailures());
                        callTracker.OnStateChanged(BondCallState.Failed);
                        return new RpcCallResult<T>(RpcCallResultState.Failed, retryCount + 1, callTracker.TotalDuration, waitForConnectionDuration);
                    }

                    if (DistributionServices.IsBuildIdMismatchException(ex))
                    {
                        m_proxyLogger.LogCallException(m_loggingContext, functionName, retryCount, ex);

                        // If a message with different build is received, it means that the sender has participated in a different distributed build.
                        // Then, we need to lose the connection with the sender.
                        OnConnectionTimeOut?.Invoke(this, EventArgs.Empty);
                        return new RpcCallResult<T>(RpcCallResultState.Failed, retryCount + 1, callTracker.TotalDuration, waitForConnectionDuration);
                    }

                    // If not a transient exception, log and throw
                    if (!DistributionHelpers.IsTransientBondException(ex, m_services.Counters) && !m_services.IsChecksumMismatchException(ex))
                    {
                        m_proxyLogger.LogCallException(m_loggingContext, functionName, retryCount, ex);
                        throw;
                    }

                    // Otherwise, the exception is transient, so log exception and try again
                    lastFailure = new RecoverableExceptionFailure(new BuildXLException("Failed Bond RPC call", ex));
                    callTracker.LogMessage("{0}", lastFailure.DescribeIncludingInnerFailures());

                    // Deactivate connection so subsequent calls on the proxy will wait for heartbeat before trying to make call.
                    DeactivateConnection(connection);

                    m_services.Counters.AddToCounter(DistributionCounter.FailedSendPipBuildRequestCallDurationMs, (long)callTracker.TotalDuration.TotalMilliseconds);
                    m_services.Counters.IncrementCounter(DistributionCounter.FailedSendPipBuildRequestCount);
                    m_proxyLogger.LogFailedCall(m_loggingContext, functionName, retryCount, lastFailure);
                }
                finally
                {
                    m_outstandingCalls.AddOrUpdate(functionName, 0, (k, i) => i - 1);
                }
            }

            // Exceeded retry count.
            callTracker.LogMessage("Call failed and exhausted allowed retries. LastFailure={0}", lastFailure?.DescribeIncludingInnerFailures() ?? string.Empty);
            callTracker.OnStateChanged(BondCallState.Failed);
            return new RpcCallResult<T>(RpcCallResultState.Failed, DefaultMaxRetryCount, callTracker.TotalDuration, waitForConnectionDuration, lastFailure);
        }

        /// <summary>
        /// Helper to allow cancellation on waiting for the connection to become active
        /// </summary>
        private async Task<bool> WaitForActive(CancellationToken cancellationToken)
        {
            var isActiveTask = m_isActiveCompletionSource.Task;
            if (isActiveTask.IsCompleted && isActiveTask.Result)
            {
                return true;
            }

            var cancellationTask = Task.Delay(Timeout.Infinite, cancellationToken);
            var completedTask = await Task.WhenAny(isActiveTask, cancellationTask);

            await completedTask;

            // Yield to ensure that we don't run the continuation of the completion source synchronously
            await Task.Yield();

            // If we reach this point, the isActive task completed rather than the cancellation
            return isActiveTask.Result;
        }

        /// <summary>
        /// Waits for the connection to become active via a successful heartbeat. Heartbeats pass allowInactive to skip actually waiting.
        /// </summary>
        private async Task<TrackedConnectionScope> WaitForConnectionAsync(BondCallTracker callTracker, bool allowInactive, CancellationToken cancellationToken)
        {
            if (m_exceededInactivityTimeout)
            {
                return default(TrackedConnectionScope);
            }

            if (!allowInactive)
            {
                bool isActive = await WaitForActive(cancellationToken);
                if (!isActive)
                {
                    return default(TrackedConnectionScope);
                }
            }

            // TODO: Should the amount of concurrency be limited?
            await m_proxySemaphore.WaitAsync(cancellationToken);

            // Cycle through the connections to distribute the load of the calls
            var takeIndex = Interlocked.Increment(ref m_takeIndex) % m_maxConnectionConcurrency;

            var connection = m_connections[takeIndex];
            Contract.Assert(connection != null);

            bool connected = await connection.ConnectAndPinAsync(callTracker, cancellationToken);

            if (!connected)
            {
                return default(TrackedConnectionScope);
            }

            return new TrackedConnectionScope(connection);
        }

        private void DeactivateConnection(TrackedConnection connection)
        {
            lock (m_syncLock)
            {
                if (m_isActiveCompletionSource.Task.IsCompleted)
                {
                    if (m_isActiveCompletionSource.Task.Result)
                    {
                        m_isActiveCompletionSource = TaskSourceSlim.Create<bool>();
                    }
                }

                if (m_exceededInactivityTimeout)
                {
                    m_isActiveCompletionSource.TrySetResult(false);
                }
            }

            OnDeactivateConnection?.Invoke(this, EventArgs.Empty);

            // TODO: Do nothing to actual connection for now.
            // Consider recycling connection (ie create new BondTcpConnection) if
            // connection fails too many times or hasn't had a successful call for a significant
            // interval.
            // NOTE: connection may be null
            Analysis.IgnoreArgument(connection);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA1816:CallGCSuppressFinalizeCorrectly")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1063:ImplementIDisposableCorrectly")]
        public void Dispose()
        {
            Terminate();

            lock (m_syncLock)
            {
                m_isShuttingDown = true;
                m_heartbeatTimer.Dispose();
                foreach (var connection in m_connections)
                {
                    connection.Dispose();
                }

                m_proxySemaphore.Dispose();
            }
        }

        private struct TrackedConnectionScope : IDisposable
        {
            public readonly TrackedConnection Connection;
            private bool m_succeeded;

            public TrackedConnectionScope(TrackedConnection connection)
            {
                m_succeeded = false;
                Connection = connection;
            }

            public void MarkSucceeded()
            {
                m_succeeded = true;
                Connection?.MarkSuccessOrRecreated();
            }

            public void Dispose()
            {
                if (!m_succeeded)
                {
                    if (Connection != null)
                    {
                        // Operation did not succeed so recreate unless a successful call goes through
                        Connection.RecreateOnNextAccessAfterTimeout = true;
                    }
                }

                Connection?.Release();
            }
        }

        /// <summary>
        /// Tracks a conneciton
        /// </summary>
        private sealed class TrackedConnection : IDisposable
        {
            private bool m_isDisposed;
            private BondTcpConnection<TProxy> m_connection;
            private readonly BondProxyConnectionManager<TProxy> m_connectionManager;
            private readonly SemaphoreSlim m_connectionSemaphore;
            private TimeSpan m_lastSuccessTimestamp;
            public bool RecreateOnNextAccessAfterTimeout = false;

            private bool ShouldRecreate => RecreateOnNextAccessAfterTimeout
                && (TimestampUtilities.Timestamp - m_lastSuccessTimestamp) > ConnectionRefreshTimeout;

            public TrackedConnection(BondProxyConnectionManager<TProxy> connectionManager)
            {
                m_connectionManager = connectionManager;

                // Mutex to ensure only one call is modifying connection at a time
                m_connectionSemaphore = new SemaphoreSlim(1);
            }

            public TProxy Proxy
            {
                get
                {
                    var connection = m_connection;
                    if (m_isDisposed)
                    {
                        // Throw ObjectDisposedException which will cause RPC call to fail gracefully
                        throw new ObjectDisposedException(nameof(TrackedConnection));
                    }

                    Contract.Assert(connection != null);
                    return connection.Proxy;
                }
            }

            public async Task<bool> ConnectAndPinAsync(BondCallTracker callTracker, CancellationToken cancellationToken)
            {
                using (await m_connectionSemaphore.AcquireAsync(cancellationToken))
                {
                    var connection = m_connection;
                    if (connection == null || ShouldRecreate)
                    {
                        if (m_isDisposed)
                        {
                            return false;
                        }

                        callTracker.OnStateChanged(BondCallState.RecreateConnection);
                        connection = await RecreateConnection(connection);
                        lock (this)
                        {
                            if (m_isDisposed)
                            {
                                connection.Dispose();
                                return false;
                            }

                            m_connection = connection;
                        }
                    }
                }

                return true;
            }

            private async Task<BondTcpConnection<TProxy>> RecreateConnection(BondTcpConnection<TProxy> connection)
            {
                connection = await m_connectionManager.CreateConnectionAsync();
                MarkSuccessOrRecreated();
                return connection;
            }

            public void Release()
            {
                m_connectionManager.m_proxySemaphore.Release();
            }

            public void Dispose()
            {
                m_isDisposed = true;

                lock (this)
                {
                    m_connection?.Dispose();
                }
            }

            public void MarkSuccessOrRecreated()
            {
                RecreateOnNextAccessAfterTimeout = false;
                m_lastSuccessTimestamp = TimestampUtilities.Timestamp;
            }
        }
    }
}
#endif
