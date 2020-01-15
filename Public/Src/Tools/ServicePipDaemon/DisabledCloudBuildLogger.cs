// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Tracing.CloudBuild;

namespace Tool.ServicePipDaemon
{
    /// <summary>
    ///     Implementation of <see cref="ICloudBuildLogger"/> that doesn't do anything.
    /// </summary>
    public sealed class DisabledCloudBuildLogger : ICloudBuildLogger
    {
        /// <inheritdoc/>
        public void Log(DropFinalizationEvent e) { }

        /// <inheritdoc/>
        public void Log(DropCreationEvent e) { }
    }
}
