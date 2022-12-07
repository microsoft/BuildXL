// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.Serialization;

namespace BuildXL.Cache.Host.Configuration
{
    /// <summary>
    /// Describes retry policies that we have common configuration and code for
    /// </summary>
    public enum StandardRetryPolicy
    {
        /// <nodoc />
        ExponentialBackoff,

        /// <nodoc />
        ExponentialSpread,
    }

    public record RetryPolicyConfiguration
    {
        /// <summary>The default number of retry attempts.</summary>
        public static readonly int DefaultRetryCount = 10;

        /// <summary>
        /// The default minimum amount of time used when calculating the exponential delay between retries.
        /// </summary>
        public static readonly TimeSpan DefaultMinimumRetryWindow = TimeSpan.FromSeconds(1.0);

        /// <summary>
        /// The default maximum amount of time used when calculating the exponential delay between retries.
        /// </summary>
        public static readonly TimeSpan DefaultMaximumRetryWindow = TimeSpan.FromSeconds(30.0);

        /// <summary>
        /// The default amount of time used when calculating a random delta in the exponential delay between retries.
        /// </summary>
        public static readonly TimeSpan DefaultExponentialBackoffDelta = TimeSpan.FromSeconds(10.0);

        /// <summary>
        /// The default spread used when calculating a random delta in the exponential delay between retries.
        /// </summary>
        public static readonly double DefaultWindowJitter = 0.4;
        
        public EnumSetting<StandardRetryPolicy> RetryPolicy { get; init; } = StandardRetryPolicy.ExponentialBackoff;

        public TimeSpanSetting MinimumRetryWindow { get; init; } = DefaultMinimumRetryWindow;

        public TimeSpanSetting MaximumRetryWindow { get; init; } = DefaultMaximumRetryWindow;

        public TimeSpanSetting ExponentialBackoffDelta { get; init; } = DefaultExponentialBackoffDelta;

        public double WindowJitter { get; init; } = DefaultWindowJitter;

        public int? MaximumRetryCount { get; init; }

        public static RetryPolicyConfiguration Exponential(
            TimeSpan? minimumRetryWindow = null,
            TimeSpan? maximumRetryWindow = null,
            TimeSpan? delta = null,
            double? spread = null,
            int? maximumRetryCount = null)
        {
            return new RetryPolicyConfiguration()
            {
                RetryPolicy = StandardRetryPolicy.ExponentialBackoff,
                MinimumRetryWindow = minimumRetryWindow ?? DefaultMinimumRetryWindow,
                MaximumRetryWindow = maximumRetryWindow ?? DefaultMaximumRetryWindow,
                ExponentialBackoffDelta = delta ?? DefaultExponentialBackoffDelta,
                WindowJitter = spread ?? DefaultWindowJitter,
                MaximumRetryCount = maximumRetryCount,
            };
        }
    }
}
