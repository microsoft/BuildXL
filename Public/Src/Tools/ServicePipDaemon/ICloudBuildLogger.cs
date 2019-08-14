// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Tracing.CloudBuild;

namespace Tool.ServicePipDaemon
{
    /// <summary>
    ///     Interface for sending ETW events.
    /// </summary>
    public interface ICloudBuildLogger
    {
        /// <nodoc/>
        void Log(DropCreationEvent e);

        /// <nodoc/>
        void Log(DropFinalizationEvent e);
    }

    /// <summary>
    ///     Extensions for <see cref="ICloudBuildLogger"/>.
    /// </summary>
    public static class CloudBuildLoggerExtensions
    {
        /// <summary>
        ///     Logs a generic <see cref="DropOperationBaseEvent"/> by delegating based on the type of <paramref name="e"/>.
        /// </summary>
        public static void Log(this ICloudBuildLogger logger, DropOperationBaseEvent e)
        {
            switch (e.Kind)
            {
                case EventKind.DropCreation:
                    logger.Log((DropCreationEvent)e);
                    return;

                case EventKind.DropFinalization:
                    logger.Log((DropFinalizationEvent)e);
                    return;

                default:
                    Contract.Assert(false, "Unsupported " + nameof(DropOperationBaseEvent) + " " + e.Kind);
                    return;
            }
        }
    }
}
