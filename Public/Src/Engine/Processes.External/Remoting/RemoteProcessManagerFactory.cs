// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;

#nullable enable

namespace BuildXL.Processes.Remoting
{
    /// <summary>
    /// Factory for <see cref="IRemoteProcessManager"/>.
    /// </summary>
    /// <remarks>
    /// Process remoting is not currently supported. To add a new remoting service:
    /// 1. Implement <see cref="IRemoteProcessManager"/> for the new service (see the IRemoteProcessPip and
    ///    IRemoteProcessPipResult interfaces for the contract each remote process must fulfill).
    /// 2. Implement <see cref="IRemoteProcessManagerInstaller"/> if the service requires client installation.
    /// 3. In <see cref="Create"/>, instantiate the new manager when <see cref="IScheduleConfiguration.EnableProcessRemoting"/> is true.
    /// 4. Re-enable the /enableProcessRemoting command-line flag in Args.cs.
    /// </remarks>
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
            IRemoteFilePredictor filePredictor,
            CounterCollection<SandboxedProcessFactory.SandboxedProcessCounters> counters)
        {
            if (s_preSetRemoteProcessManager != null)
            {
                return s_preSetRemoteProcessManager;
            }

            // TODO: When a new remoting service is available, check configuration.Schedule.EnableProcessRemoting
            // and instantiate the appropriate IRemoteProcessManager implementation here.
            return new NoRemotingRemoteProcessManager();
        }

        /// <summary>
        /// Pre-sets remote process manager; should only be used for testing.
        /// </summary>
        internal static void PreSetRemoteProcessManager(IRemoteProcessManager remoteProcessManager) => s_preSetRemoteProcessManager = remoteProcessManager;
    }
}
