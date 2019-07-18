// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
