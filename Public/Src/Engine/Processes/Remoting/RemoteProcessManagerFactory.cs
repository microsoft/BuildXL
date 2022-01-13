// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;

#nullable enable

namespace BuildXL.Processes.Remoting
{
    /// <summary>
    /// Selects an instance of <see cref="IRemoteProcessManager"/>.
    /// </summary>
    public class RemoteProcessManagerFactory
    {
        /// <summary>
        /// Pre-set instance of <see cref="IRemoteProcessManager"/>.
        /// </summary>
        /// <remarks>
        /// This static instance can be useful for testing purpose.
        /// </remarks>
        internal static Lazy<IRemoteProcessManager>? RemoteProcessManager;

        /// <summary>
        /// Gets or creates an instance of <see cref="IRemoteProcessManager"/>.
        /// </summary>
        public static IRemoteProcessManager GetOrCreate(
            LoggingContext loggingContext,
            PipExecutionContext executionContext,
            IConfiguration configuration,
            CounterCollection<SandboxedProcessFactory.SandboxedProcessCounters> counters)
        {
            Interlocked.CompareExchange(
                ref RemoteProcessManager,
                new Lazy<IRemoteProcessManager>(() =>
                {
#if FEATURE_ANYBUILD_PROCESS_REMOTING
                    IRemoteProcessManager remoteProcessManager = new AnyBuildRemoteProcessManager(loggingContext, executionContext, configuration, counters);
#else
                    IRemoteProcessManager remoteProcessManager = new NoRemotingRemoteProcessManager();
#endif
                    return remoteProcessManager;
                }),
                null);

            return RemoteProcessManager.Value;
        }
    }
}
