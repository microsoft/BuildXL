// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.Interfaces;
using BuildXL.Utilities.Tasks;

namespace Tool.ServicePipDaemon
{
    /// <summary>
    /// Base class for reloading clients, controls the retrying logic.
    /// </summary>
    public abstract class ReloadingClient<T> : IDisposable where T : IDisposable
    {
        private static readonly TimeSpan s_defaultOperationTimeout = TimeSpan.FromMinutes(15);

        // Default number and length of polling intervals - total time is approximately the sum of all these intervals.
        // NOTE: taken from DBS.ActionRetryer class, from CloudBuild.Core
        private static readonly IEnumerable<TimeSpan> s_defaultRetryIntervals = new[]
        {
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(200),
            TimeSpan.FromMilliseconds(200),
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(4),
            TimeSpan.FromSeconds(8),
            TimeSpan.FromSeconds(16),
            TimeSpan.FromSeconds(32),
            // Total just over 1 minute.
        };

        private readonly IIpcLogger m_logger;
        private readonly IEnumerable<TimeSpan> m_retryIntervals;
        private readonly HashSet<Type> m_nonRetryableExceptions;

        /// <summary>
        /// Exposed for testing
        /// </summary>
        internal readonly Reloader<T> Reloader;

        /// <nodoc />
        public ReloadingClient(IIpcLogger logger, Func<T> clientConstructor, IEnumerable<TimeSpan> retryIntervals = null, IEnumerable<Type> nonRetryableExceptions = null)
        {
            Contract.Assert(logger != null);
            Contract.Assert(clientConstructor != null);
            Contract.Assert(nonRetryableExceptions == null || nonRetryableExceptions.All(e => e.IsSubclassOf(typeof(Exception))));

            m_logger = logger;
            Reloader = new Reloader<T>(clientConstructor, destructor: client => client.Dispose());
            m_retryIntervals = retryIntervals ?? s_defaultRetryIntervals;
            m_nonRetryableExceptions = nonRetryableExceptions == null ? new HashSet<Type>() : new HashSet<Type>(nonRetryableExceptions);
        }

        /// <summary>
        /// Executes and, if necessary, retries an operation 
        /// </summary>        
        protected async Task<U> RetryAsync<U>(
            string operationName,
            Func<T, CancellationToken, Task<U>> fn,
            CancellationToken cancellationToken,
            IEnumerator<TimeSpan> retryIntervalEnumerator = null,
            bool reloadFirst = false,
            Guid? operationId = null,
            TimeSpan? timeout = null)
        {
            operationId = operationId ?? Guid.NewGuid();
            retryIntervalEnumerator = retryIntervalEnumerator ?? m_retryIntervals.GetEnumerator();
            timeout = timeout ?? s_defaultOperationTimeout;

            try
            {
                using (CancellationTokenSource timeoutCancellationSource = new CancellationTokenSource())
                using (CancellationTokenSource innerCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCancellationSource.Token))
                {
                    var instance = GetCurrentVersionedValue();

                    if (reloadFirst)
                    {
                        var reloaded = Reloader.Reload(instance.Version);
                        m_logger.Warning("[{2}] Service client reloaded; new instance created: {0}, new client version: {1}", reloaded, Reloader.CurrentVersion, operationId.Value);
                    }

                    m_logger.Verbose("[{2}] Invoking '{0}' against instance version {1}", operationName, instance.Version, operationId.Value);
                    return await WithTimeoutAsync(fn(instance.Value, innerCancellationSource.Token), timeout.Value, timeoutCancellationSource);
                }
            }
            catch (Exception e) when (m_nonRetryableExceptions.Contains(e.GetType()))
            {
                // We should not retry exceptions of this type.
                throw;
            }
            catch (Exception e)
            {
                if (e is TimeoutException)
                {
                    m_logger.Warning("Timeout ({0}sec) happened while waiting {1}.", timeout.Value.TotalSeconds, operationName);
                }

                if (retryIntervalEnumerator.MoveNext())
                {
                    m_logger.Warning("[{2}] Waiting {1} before retrying on exception: {0}", e.ToString(), retryIntervalEnumerator.Current, operationId.Value);
                    await Task.Delay(retryIntervalEnumerator.Current);
                    return await RetryAsync(operationName, fn, cancellationToken, retryIntervalEnumerator, reloadFirst: true, operationId: operationId);
                }
                else
                {
                    m_logger.Error("[{1}] Failing because number of retries were exhausted.  Final exception: {0};", e.ToString(), operationId.Value);
                    throw;
                }
            }
        }

        /// <summary>
        /// Executes and, if necessary, retries an operation 
        /// </summary>
        protected Task RetryAsync(string operationName, Func<T, CancellationToken, Task> fn, CancellationToken token)
        {
            return RetryAsync(
                operationName,
                async (client, t) =>
                {
                    await fn(client, t);
                    return Unit.Void;
                },
                token);
        }

        private static async Task<U> WithTimeoutAsync<U>(Task<U> task, TimeSpan timeout, CancellationTokenSource timeoutToken)
        {
            if (timeout != Timeout.InfiniteTimeSpan)
            {
                if (await Task.WhenAny(task, Task.Delay(timeout)) != task)
                {
                    timeoutToken.Cancel();
                    throw new TimeoutException();
                }
            }

            return await task;
        }

        /// <summary>
        /// The Current version of the underlying client.
        /// </summary>        
        protected Reloader<T>.VersionedValue GetCurrentVersionedValue()
        {
            Reloader.EnsureLoaded();
            return Reloader.CurrentVersionedValue;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Reloader.Dispose();
        }
    }
}
