// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;

#nullable enable

namespace BuildXL.Processes.Remoting
{
    /// <summary>
    /// Factor for <see cref="IRemoteProcessManager"/>.
    /// </summary>
    public class RemoteProcessManagerFactory
    {
        /// <summary>
        /// Pre-set instance of <see cref="IRemoteProcessManager"/>.
        /// </summary>
        /// <remarks>
        /// This static instance should only be used for testing purpose.
        /// </remarks>
        private static IRemoteProcessManager? s_preSetRemoteProcessManager;

        /// <summary>
        /// Creates an instance of <see cref="IRemoteProcessManager"/>.
        /// </summary>
        /// <remarks>
        /// For one build session, there should only be one instance of <see cref="IRemoteProcessManager"/>.
        /// To that end, this method should only be called once. Currently, it is called from the scheduler. The obtained
        /// instance of <see cref="IRemoteProcessManager"/> will be disposed when the scheduler is disposed as well.
        /// One cannot use the singleton pattern here and have a get-or-create method because BuildXL
        /// can run as a server; otherwise the singleton instance will be used across build sessions.
        /// </remarks>
        public static IRemoteProcessManager Create(
            LoggingContext loggingContext,
            PipExecutionContext executionContext,
            IConfiguration configuration,
            CounterCollection<SandboxedProcessFactory.SandboxedProcessCounters> counters)
        {
            if (s_preSetRemoteProcessManager != null)
            {
                return s_preSetRemoteProcessManager;
            }

#if FEATURE_ANYBUILD_PROCESS_REMOTING
            IRemoteProcessManager remoteProcessManager = new AnyBuildRemoteProcessManager(loggingContext, executionContext, configuration, counters);
#else
            IRemoteProcessManager remoteProcessManager = new NoRemotingRemoteProcessManager();
#endif
            return remoteProcessManager;
        }

        /// <summary>
        /// Pre-sets remote process manager; should only be used for testing.
        /// </summary>
        internal static void PreSetRemoteProcessManager(IRemoteProcessManager remoteProcessManager) => s_preSetRemoteProcessManager = remoteProcessManager;
    }
}
