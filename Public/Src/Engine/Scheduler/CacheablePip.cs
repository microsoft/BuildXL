// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Adds behavior for caching pip on to data for pip caching
    /// </summary>
    public abstract class CacheablePip : CacheablePipInfo
    {
        /// <nodoc />
        protected CacheablePip(
            Pip pip,
            PipExecutionContext context,
            ReadOnlyArray<FileArtifactWithAttributes> outputs,
            ReadOnlyArray<FileArtifact> dependencies,
            ReadOnlyArray<DirectoryArtifact> directoryOutputs,
            ReadOnlyArray<DirectoryArtifact> directoryDependencies)
            : base(pip, context, outputs, dependencies, directoryOutputs, directoryDependencies)
        {
        }

        /// <summary>
        /// Computes the weak fingerprint for the pip
        /// </summary>
        public abstract ContentFingerprint ComputeWeakFingerprint();

        /// <summary>
        /// Indicates that the pip should be cached
        /// </summary>
        public abstract bool ShouldHaveArtificialMiss();

        /// <summary>
        /// Whether the pip is configured to miss on cache lookup
        /// </summary>
        public abstract bool DisableCacheLookup();
    }
}
