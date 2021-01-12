// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using Polly;
using Polly.Contrib.WaitAndRetry;

namespace BuildXL.Cache.ContentStore.Utils
{
    /// <summary>
    /// Provides helpers for creating retry policies.
    /// </summary>
    public static class RetryPolicyFactory
    {
        /// <summary>The default number of retry attempts.</summary>
        public static readonly int DefaultRetryCount = 10;

        /// <summary>
        /// The default minimum amount of time used when calculating the exponential delay between retries.
        /// </summary>
        public static readonly TimeSpan DefaultMinBackoff = TimeSpan.FromSeconds(1.0);

        /// <summary>
        /// The default maximum amount of time used when calculating the exponential delay between retries.
        /// </summary>
        public static readonly TimeSpan DefaultMaxBackoff = TimeSpan.FromSeconds(30.0);

        /// <summary>
        /// The default amount of time used when calculating a random delta in the exponential delay between retries.
        /// </summary>
        public static readonly TimeSpan DefaultDeltaBackoff = TimeSpan.FromSeconds(10.0);

        private static IEnumerable<TimeSpan> GetDefaultExponentialBackoff() =>
            Backoff.DecorrelatedJitterBackoffV2(medianFirstRetryDelay: DefaultMinBackoff, retryCount: DefaultRetryCount)
                .Select(ts => new TimeSpan(ticks: Math.Min(ts.Ticks, DefaultMaxBackoff.Ticks)));

        private static IEnumerable<TimeSpan> GetExponentialBackoff(int retryCount, TimeSpan minBackoff, TimeSpan maxBackoff, TimeSpan deltaBackoff)
        {
            return Enumerable.Range(0, retryCount)
                // Formula copied from TransientFaultHandling
                .Select(currentRetryCount => minBackoff.TotalMilliseconds + ((Math.Pow(2.0, currentRetryCount) - 1.0) * ThreadSafeRandom.Generator.Next((int)(deltaBackoff.TotalMilliseconds * 0.8), (int)(deltaBackoff.TotalMilliseconds * 1.2))))
                .Select(ms => Math.Min(ms, maxBackoff.TotalMilliseconds))
                .Select(ms => TimeSpan.FromMilliseconds(ms));
        }

        /// <nodoc />
        public static IRetryPolicy GetExponentialPolicy(Func<Exception, bool> shouldRetry)
        {
            return new PollyRetryPolicy(GetDefaultExponentialBackoff, shouldRetry);
        }

        /// <nodoc />
        public static IRetryPolicy GetExponentialPolicy(Func<Exception, bool> shouldRetry, int retryCount, TimeSpan minBackoff, TimeSpan maxBackoff, TimeSpan deltaBackoff)
        {
            return new PollyRetryPolicy(() => GetExponentialBackoff(retryCount, minBackoff, maxBackoff, deltaBackoff), shouldRetry);
        }

        /// <nodoc />
        public static IRetryPolicy GetLinearPolicy(Func<Exception, bool> shouldRetry, int retries, TimeSpan? retryInterval = null)
        {
            retryInterval ??= DefaultMinBackoff;
            return new PollyRetryPolicy(() => Enumerable.Range(0, retries).Select(_ => retryInterval.Value), shouldRetry);
        }
    }

    /// <summary>
    /// Policy used to determine which errors to retry and how much retries to perform.
    /// </summary>
    public interface IRetryPolicy
    {
        /// <nodoc />
        Task<T> ExecuteAsync<T>(Func<Task<T>> func, CancellationToken token);

        /// <nodoc />
        Task ExecuteAsync(Func<Task> func, CancellationToken token);
    }

    /// <nodoc />
    internal class PollyRetryPolicy : IRetryPolicy
    {
        private readonly Func<IEnumerable<TimeSpan>> _generator;
        private readonly Func<Exception, bool> _shouldRetry;

        /// <nodoc />
        public PollyRetryPolicy(Func<IEnumerable<TimeSpan>> generator, Func<Exception, bool> shouldRetry)
        {
            _generator = generator;
            _shouldRetry = shouldRetry;
        }

        /// <inheritdoc />
        public Task<T> ExecuteAsync<T>(Func<Task<T>> func, CancellationToken token)
        {
            return Policy
                .Handle(_shouldRetry)
                .WaitAndRetryAsync(_generator())
                .ExecuteAsync(_ => func(), token);
        }

        /// <inheritdoc />
        public Task ExecuteAsync(Func<Task> func, CancellationToken token)
        {
            return Policy
                .Handle(_shouldRetry)
                .WaitAndRetryAsync(_generator())
                .ExecuteAsync(_ => func(), token);
        }
    }
}
