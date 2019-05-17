// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using JetBrains.Annotations;

namespace BuildXL.Processes.Containers
{
    /// <summary>
    /// Wraps a job object in a Helium container, where some paths can be virtualized using
    /// Bind and WCI filters
    /// </summary>
    public sealed class Container : JobObject
    {
        private readonly ContainerConfiguration m_containerConfiguration;
        private readonly LoggingContext m_loggingContext;

        /// <nodoc/>
        public Container([CanBeNull] string name, ContainerConfiguration containerConfiguration, [CanBeNull] LoggingContext loggingContext) : base(name)
        {
            Contract.Requires(containerConfiguration.IsIsolationEnabled);
            m_containerConfiguration = containerConfiguration;

            // Logging context is null for some tests
            m_loggingContext = loggingContext ?? new LoggingContext("Test");
        }

        /// <summary>
        /// Starts the container, attaching it to the associated job object
        /// </summary>
        /// <exception cref="BuildXLException">If the container is not setup properly</exception>
        /// <remarks>
        /// This operation is detached from the actual construction of the Container since the container has to be started
        /// after calling <see cref="JobObject.SetLimitInformation(bool?, System.Diagnostics.ProcessPriorityClass?, bool)"/>
        /// </remarks>
        public override void StartContainerIfPresent()
        {
            Native.Processes.ProcessUtilities.AttachContainerToJobObject(
                            handle,
                            m_containerConfiguration.RedirectedDirectories,
                            m_containerConfiguration.EnableWciFilter,
                            m_containerConfiguration.BindFltExcludedPaths.Select(p => p.ToString()),
                            out var warnings);

            // Log any warnings when setting up the container (at this point this is just WCI retries)
            foreach (var warning in warnings)
            {
                Tracing.Logger.Log.WarningSettingUpContainer(m_loggingContext, handle.ToString(), warning);
            }
        }

        /// <summary>
        /// Cleans up the container before releasing the base class handler
        /// </summary>
        protected override bool ReleaseHandle()
        {
            if (!Native.Processes.ProcessUtilities.TryCleanUpContainer(handle, out var warnings))
            {
                foreach (var warning in warnings)
                {
                    // This is logged as a warning
                    Tracing.Logger.Log.FailedToCleanUpContainer(m_loggingContext, handle.ToString(), warning);
                }
            }

            return base.ReleaseHandle();
        }
    }
}
