// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.Host.Configuration;
using static BuildXL.Utilities.ConfigurationHelper;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache.CopyScheduling
{
    /// <nodoc />
    public record CopySchedulerConfiguration
    {
        /// <nodoc />
        public CopySchedulerType Type { get; init; }

        /// <nodoc />
        public DefaultCopySchedulerConfiguration DefaultCopySchedulerConfiguration { get; init; } = new DefaultCopySchedulerConfiguration();

        /// <nodoc />
        public PrioritizedCopySchedulerConfiguration PrioritizedCopySchedulerConfiguration { get; init; } = new PrioritizedCopySchedulerConfiguration();

        /// <nodoc />
        public static CopySchedulerConfiguration FromDistributedContentSettings(DistributedContentSettings distributedSettings)
        {
            return new CopySchedulerConfiguration()
            {
                Type = ParseEnumOrDefault(
                    distributedSettings.CopySchedulerType,
                    nameof(distributedSettings.CopySchedulerType),
                    CopySchedulerType.Default),
                DefaultCopySchedulerConfiguration = new DefaultCopySchedulerConfiguration()
                {
                    OutboundPullConfiguration = new CopySemaphoreConfiguration()
                    {
                        MaximumConcurrency = distributedSettings.MaxConcurrentCopyOperations,
                        SemaphoreOrder = ParseEnumOrDefault(
                            distributedSettings.OrderForCopies,
                            nameof(distributedSettings.OrderForCopies),
                            SemaphoreOrder.NonDeterministic),
                        // There's no pre-set timeout for passive copies
                        WaitTimeout = null,
                    },
                    OutboundPushConfiguration = new CopySemaphoreConfiguration()
                    {
                        MaximumConcurrency = distributedSettings.MaxConcurrentProactiveCopyOperations,
                        SemaphoreOrder = ParseEnumOrDefault(
                            distributedSettings.OrderForProactiveCopies,
                            nameof(distributedSettings.OrderForProactiveCopies),
                            SemaphoreOrder.NonDeterministic),
                        WaitTimeout = TimeSpan.FromSeconds(distributedSettings.ProactiveCopyIOGateTimeoutSeconds),
                    },
                },
                PrioritizedCopySchedulerConfiguration = distributedSettings.PrioritizedCopySchedulerConfiguration ?? new PrioritizedCopySchedulerConfiguration(),
            };
        }

        /// <nodoc />
        public ICopyScheduler Create(Context context, IClock? clock = null)
        {
            clock ??= SystemClock.Instance;

            switch (Type)
            {
                case CopySchedulerType.Default:
                    return new DefaultCopyScheduler(DefaultCopySchedulerConfiguration, context);
                case CopySchedulerType.Prioritized:
                    return new PrioritizedCopyScheduler(PrioritizedCopySchedulerConfiguration, clock);
                default:
                    throw new NotImplementedException($"Unknown {nameof(ICopyScheduler)} version `{Type}`");
            }
        }
    }
}
