// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Base class for facilitating running services.
    /// </summary>
    public class ServiceManager
    {
        /// <summary>
        /// Default ServiceManager which only works for pips that have no service dependencies.
        /// </summary>
        public static readonly ServiceManager Default = new ServiceManager();

        /// <summary>
        /// Executes the service dependencies for the given process
        /// </summary>
        public virtual Task<bool> TryRunServiceDependenciesAsync(
            IPipExecutionEnvironment environment,
            PipId pipId,
            IEnumerable<PipId> servicePips,
            LoggingContext loggingContext)
        {
            // NOTE: This contract assertion cannot be a precondition because it only applies to the default service manager
            Contract.Assert(
                !servicePips.Any(),
                "Default service manager cannot be used to execute services and thus should not be called with processes requires service pip execution");

            return BoolTask.True;
        }

        /// <summary>
        /// Checks whether an IPC pip has a non-service pip dependent
        /// </summary>
        public virtual bool HasRealConsumers(IpcPip pip)
        {
            return false;
        }
    }
}
