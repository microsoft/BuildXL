// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// The level of isolation for a given process when running in a container
    /// </summary>
    [Flags]
    public enum ContainerIsolationLevel
    {
        /// <summary>
        /// No isolation
        /// </summary>
        None = 0,

        /// <summary>
        /// Isolate individually declared output files
        /// </summary>
        IsolateOutputFiles = 0x1,

        /// <summary>
        /// Isolate shared opaque directories
        /// </summary>
        IsolateSharedOpaqueOutputDirectories = 0x2,

        /// <summary>
        /// Isolate exclusive opaque directories
        /// </summary>
        IsolateExclusiveOpaqueOutputDirectories = 0x4,

        /// <summary>
        /// Isolate all input dependencies
        /// </summary>
        IsolateInputs = 0x8,

        /// <nodoc/>
        IsolateOutputDirectories = IsolateSharedOpaqueOutputDirectories | IsolateExclusiveOpaqueOutputDirectories,

        /// <nodoc/>
        IsolateAllOutputs = IsolateOutputFiles | IsolateOutputDirectories,

        /// <nodoc/>
        IsolateAll = IsolateAllOutputs | IsolateInputs,
    }

    /// <nodoc/>
    public static class ContainerIsolationLevelExtensions
    {
        /// <nodoc/>
        public static bool IsolateOutputFiles(this ContainerIsolationLevel containerIsolationLevel) => (containerIsolationLevel & ContainerIsolationLevel.IsolateOutputFiles) == ContainerIsolationLevel.IsolateOutputFiles;

        /// <nodoc/>
        public static bool IsolateSharedOpaqueOutputDirectories(this ContainerIsolationLevel containerIsolationLevel) => (containerIsolationLevel & ContainerIsolationLevel.IsolateSharedOpaqueOutputDirectories) == ContainerIsolationLevel.IsolateSharedOpaqueOutputDirectories;

        /// <nodoc/>
        public static bool IsolateExclusiveOpaqueOutputDirectories(this ContainerIsolationLevel containerIsolationLevel) => (containerIsolationLevel & ContainerIsolationLevel.IsolateExclusiveOpaqueOutputDirectories) == ContainerIsolationLevel.IsolateExclusiveOpaqueOutputDirectories;

        /// <nodoc/>
        public static bool IsolateOutputDirectories(this ContainerIsolationLevel containerIsolationLevel) => (containerIsolationLevel & ContainerIsolationLevel.IsolateOutputDirectories) == ContainerIsolationLevel.IsolateOutputDirectories;

        /// <nodoc/>
        public static bool IsolateInputs(this ContainerIsolationLevel containerIsolationLevel) => (containerIsolationLevel & ContainerIsolationLevel.IsolateInputs) == ContainerIsolationLevel.IsolateInputs;

        /// <nodoc/>
        public static bool IsolateAllOutputs(this ContainerIsolationLevel containerIsolationLevel) => (containerIsolationLevel & ContainerIsolationLevel.IsolateAllOutputs) == ContainerIsolationLevel.IsolateAllOutputs;
    }
}
