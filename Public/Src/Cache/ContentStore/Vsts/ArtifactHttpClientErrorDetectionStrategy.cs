// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using Microsoft.Practices.TransientFaultHandling;

namespace BuildXL.Cache.ContentStore.Vsts
{
    /// <summary>
    /// Retry strategy calls to an ArtifactHttpClient.
    /// </summary>
    /// <remarks>
    /// HACK HACK: This is an additional layer of retries specifically to catch the SSL exceptions seen in DM.
    /// Newer versions of Artifact packages have this retry automatically, but the packages deployed with the M119.0 release do not.
    /// Once the next release is completed, these retries can be removed.
    /// </remarks>
    public class ArtifactHttpClientErrorDetectionStrategy : ITransientErrorDetectionStrategy
    {
        private static readonly Lazy<RetryPolicy> LazyRetryPolicyInstance = new Lazy<RetryPolicy>(() => new RetryPolicy<ArtifactHttpClientErrorDetectionStrategy>(RetryStrategy.DefaultExponential));

        // The HTTP request time out is 5-minute
        private static readonly TimeSpan DefaultOperationTimeout = TimeSpan.FromMinutes(6);

        /// <summary>
        /// Repeatedly executes the specificed asynchronous task with the ArtifactHttpClientErrorDetectionStrategy, logging attempts beyond the first.
        /// </summary>
        public static Task ExecuteAsync(Context context, string operationName, Func<Task> taskFunc, CancellationToken ct)
        {
            int attemptCount = 0;
            return LazyRetryPolicyInstance.Value.ExecuteAsync(
                () =>
                {
                    attemptCount++;
                    if (attemptCount > 1)
                    {
                        context.TraceMessage(Severity.Debug, $"{operationName} attempt #{attemptCount}...");
                    }

                    return taskFunc();
                }, ct);
        }

        /// <summary>
        /// Repeatedly executes the specificed asynchronous task with the ArtifactHttpClientErrorDetectionStrategy, logging attempts beyond the first.
        /// </summary>
        public static Task<T> ExecuteAsync<T>(Context context, string operationName, Func<Task<T>> taskFunc, CancellationToken ct)
        {
            int attemptCount = 0;
            return LazyRetryPolicyInstance.Value.ExecuteAsync(
                () =>
                {
                    attemptCount++;
                    if (attemptCount > 1)
                    {
                        context.TraceMessage(Severity.Debug, $"{operationName} attempt #{attemptCount}...");
                    }

                    return taskFunc();
                }, ct);
        }

        /// <summary>
        /// Repeatedly executes the specified asynchronous task with the ArtifactHttpClientErrorDetectionStrategy, logging attempts beyond the first.
        /// </summary>
        public static async Task<T> ExecuteWithTimeoutAsync<T>(Context context, string operationName, Func<CancellationToken, Task<T>> taskFunc, CancellationToken ct, TimeSpan? timeout = null)
        {
            timeout = timeout ?? DefaultOperationTimeout;

            int attemptCount = 0;
            return await LazyRetryPolicyInstance.Value.ExecuteAsync(
                async () =>
                {
                    // We need to use a fresh cancellation token in retries; otherwise, the operation will continuously get cancelled after the first timeout. 
                    using (CancellationTokenSource timeoutCancellationSource = new CancellationTokenSource())
                    using (CancellationTokenSource innerCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCancellationSource.Token))
                    {
                        if (timeout.Value != Timeout.InfiniteTimeSpan)
                        {
                            timeoutCancellationSource.CancelAfter(timeout.Value);
                        }

                        try
                        {
                            attemptCount++;
                            if (attemptCount > 1)
                            {
                                context.TraceMessage(Severity.Info, $"{operationName} attempt #{attemptCount}...");
                            }

                            return await WithTimeoutAsync(taskFunc(innerCancellationSource.Token), timeout.Value, operationName);
                        }
                        catch (OperationCanceledException)
                        {
                            if (timeoutCancellationSource.IsCancellationRequested)
                            {
                                throw new TimeoutException($"{operationName} canceled. Timeout is '{timeout}'.");
                            }

                            throw;
                        }
                        catch (TimeoutException)
                        {
                            throw;
                        }
                    }
                }, ct);
            
        }

        /// <summary>
        /// Repeatedly executes the specified asynchronous task with the ArtifactHttpClientErrorDetectionStrategy, logging attempts beyond the first.
        /// </summary>
        public static Task ExecuteWithTimeoutAsync(Context context, string operationName, Func<CancellationToken, Task> taskFunc, CancellationToken ct, TimeSpan? timeout = null)
        {
            return ExecuteWithTimeoutAsync<Object>(
                context,
                operationName,
                async (innerCt) =>
                {
                    await taskFunc(innerCt);
                    return null;
                },
                ct,
                timeout);
        }

        /// <inheritdoc />
        public bool IsTransient(Exception ex)
        {
            if (ex is TimeoutException)
            {
                return true;
            }

            if (ex is HttpRequestException)
            {
                if (ex.InnerException is WebException ||
                    (ex.InnerException is IOException && ex.InnerException.Message.Contains("Unable to read data from the transport connection")))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Waits for the given task to complete within the given timeout, throwing a <see cref="TimeoutException"/> if the timeout expires before the task completes
        /// </summary>
        private static async Task<T> WithTimeoutAsync<T>(Task<T> task, TimeSpan timeout, string operationName)
        {
            if (timeout != Timeout.InfiniteTimeSpan)
            {
                var timeoutTask = Task.Delay(timeout);
                
                if (await Task.WhenAny(task, timeoutTask) != task)
                {
                    // Ignoring exceptions thrown by task which timed out
                    Ignore(task.ContinueWith(t =>
                        {
                            Ignore(t.Exception);
                        },
                        TaskContinuationOptions.OnlyOnFaulted));

                    throw new TimeoutException($"{operationName} has timed out. Timeout is '{timeout}'.");
                }
            }

            return await task;
        }

        private static void Ignore(object value)
        {
        }
    }
}
