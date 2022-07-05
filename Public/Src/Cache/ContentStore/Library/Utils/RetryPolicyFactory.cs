// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Utilities;
using Polly;
using Polly.Contrib.WaitAndRetry;

namespace BuildXL.Cache.ContentStore.Utils
{
    /// <summary>
    /// Interface for a retry policy that can be easily instantiated by our <see cref="RetryPolicyFactory"/>. This is
    /// meant to help simplify how we add and configure ad-hoc retry policies across our code.
    /// </summary>
    public interface IStandardRetryPolicy
    {
        /// <summary>
        /// Return how long to wait, given the current retry number
        /// </summary>
        public TimeSpan Compute(int retry);
    }

    /// <summary>
    /// This is a classic exponential backoff policy. When an attempt fails, we will basically wait
    /// 2^attempt * delta milliseconds, with some jitter to help decorrelate concurrent attempts.
    /// </summary>
    public class ExponentialBackoffStandardRetryPolicy : IStandardRetryPolicy
    {
        private readonly RetryPolicyConfiguration _configuration;

        /// <nodoc />
        public ExponentialBackoffStandardRetryPolicy(RetryPolicyConfiguration configuration)
        {
            Contract.Requires(configuration.WindowJitter < 1.0);
            _configuration = configuration;
        }

        /// <nodoc />
        public TimeSpan Compute(int retry)
        {
            retry = Math.Min(retry, 32);
            // Formula copied from TransientFaultHandling
            var wndFactor = _configuration.WindowJitter / 2;
            var startWindowMs = (1.0 - wndFactor) * ((TimeSpan)_configuration.ExponentialBackoffDelta).TotalMilliseconds;
            var endWindowMs = (1.0 + wndFactor) * ((TimeSpan)_configuration.ExponentialBackoffDelta).TotalMilliseconds;
            // WARNING: it is extremely important to use ContinuousUniform here: the fact that the
            // random variable's domain is the [0, slots] \subset R instead of \subset Z greatly
            // reduces the number of retries when under contention.
            var waitTime = TimeSpan.FromMilliseconds((Math.Pow(2.0, retry) - 1) * ThreadSafeRandom.ContinuousUniform(startWindowMs, endWindowMs));
            return TimeSpanUtilities.Min(_configuration.MaximumRetryWindow, _configuration.MinimumRetryWindow + waitTime);
        }
    }

    /// <summary>
    /// This is a modified exponential backoff; it is intended to be used in scenarios where there's a high amount of
    /// contention at roughly the same time, and the computation repeats on an interval. The way it works is as
    /// follows: on each retry, we'll wait
    ///     min(maxWait, minWait + (2^r - 1) * Uniform(0, Jitter))
    /// If there's high contention, then r will tend to increase rather quickly for all involved nodes, and so the
    /// spread will increase and most operations will tend to succeed.
    /// </summary>
    public class ExponentialSpreadStandardRetryPolicy : IStandardRetryPolicy
    {
        private readonly RetryPolicyConfiguration _configuration;

        /// <nodoc />
        public ExponentialSpreadStandardRetryPolicy(RetryPolicyConfiguration configuration)
        {
            _configuration = configuration;
        }

        /// <nodoc />
        public TimeSpan Compute(int retry)
        {
            retry = Math.Min(retry, 32);
            var waitTime = TimeSpan.FromMilliseconds(Math.Pow(2.0, retry) - 1);
            var cappedWaitTime = TimeSpanUtilities.Min(_configuration.MaximumRetryWindow.Value - _configuration.MinimumRetryWindow.Value, waitTime);
            return _configuration.MinimumRetryWindow + TimeSpan.FromTicks((long)(ThreadSafeRandom.ContinuousUniform(0, 1) * cappedWaitTime.Ticks));
        }
    }

    /// <summary>
    /// Provides helpers for creating retry policies.
    /// </summary>
    public static class RetryPolicyFactory
    {
        public static IStandardRetryPolicy Create(this RetryPolicyConfiguration configuration)
        {
            return configuration.RetryPolicy.Value switch
            {
                StandardRetryPolicy.ExponentialBackoff => new ExponentialBackoffStandardRetryPolicy(configuration),
                StandardRetryPolicy.ExponentialSpread => new ExponentialSpreadStandardRetryPolicy(configuration),
                _ => throw new NotImplementedException($"Attempt to create a retry policy `{configuration.RetryPolicy.Value}`, which does not exist"),
            };
        }

        public static IRetryPolicy AsRetryPolicy(this RetryPolicyConfiguration configuration, Func<Exception, bool> shouldRetry, int maximumRetryCount)
        {
            return configuration.Create().AsRetryPolicy(shouldRetry, maximumRetryCount);
        }

        /// <nodoc />
        public static IEnumerable<TimeSpan> AsEnumerable(this IStandardRetryPolicy policy, int retryCount)
        {
            return Enumerable.Range(0, retryCount).Select(i => policy.Compute(i));
        }

        /// <nodoc />
        public static IRetryPolicy AsRetryPolicy(this IStandardRetryPolicy policy, Func<Exception, bool> shouldRetry, int maximumRetryCount)
        {
            return new PollyRetryPolicy(() => policy.AsEnumerable(maximumRetryCount), shouldRetry);
        }

        /// <nodoc />
        public static IRetryPolicy GetExponentialPolicy(Func<Exception, bool> shouldRetry)
        {
            return new PollyRetryPolicy(() =>
            {
                return Backoff.DecorrelatedJitterBackoffV2(
                    medianFirstRetryDelay: RetryPolicyConfiguration.DefaultMinimumRetryWindow,
                    retryCount: RetryPolicyConfiguration.DefaultRetryCount)
                    .Select(ts => new TimeSpan(ticks: Math.Min(ts.Ticks, RetryPolicyConfiguration.DefaultMaximumRetryWindow.Ticks)));
            }, shouldRetry);
        }

        /// <nodoc />
        public static IRetryPolicy GetExponentialPolicy(Func<Exception, bool> shouldRetry, int retryCount, TimeSpan minBackoff, TimeSpan maxBackoff, TimeSpan deltaBackoff)
        {
            return RetryPolicyConfiguration
                .Exponential(minimumRetryWindow: minBackoff, maximumRetryWindow: maxBackoff, delta: deltaBackoff)
                .AsRetryPolicy(shouldRetry, retryCount);
        }

        /// <nodoc />
        public static IRetryPolicy GetLinearPolicy(Func<Exception, bool> shouldRetry, int retries, TimeSpan? retryInterval = null)
        {
            retryInterval ??= RetryPolicyConfiguration.DefaultMinimumRetryWindow;
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

    internal class NonRetryingRetryPolicy : IRetryPolicy
    {
        public static NonRetryingRetryPolicy Instance { get; } = new NonRetryingRetryPolicy();

        protected NonRetryingRetryPolicy()
        {
        }

        public Task<T> ExecuteAsync<T>(Func<Task<T>> func, CancellationToken token)
        {
            return func();
        }

        public Task ExecuteAsync(Func<Task> func, CancellationToken token)
        {
            return func();
        }
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
