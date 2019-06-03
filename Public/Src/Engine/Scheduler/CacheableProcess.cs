// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Defines data and behavior specific for caching processes
    /// </summary>
    public class CacheableProcess : CacheablePip
    {
        /// <summary>
        /// The process
        /// </summary>
        public readonly Process Process;

        /// <summary>
        /// The execution environment
        /// </summary>
        private readonly IPipExecutionEnvironment m_environment;

        /// <nodoc />
        public CacheableProcess(Process process, IPipExecutionEnvironment environment)
            : base(
                pip: process,
                context: environment.Context,
                outputs: process.FileOutputs,
                dependencies: process.Dependencies,
                directoryOutputs: process.DirectoryOutputs,
                directoryDependencies: process.DirectoryDependencies)
        {
            Process = process;
            m_environment = environment;
        }

        /// <inheritdoc />
        public override ContentFingerprint ComputeWeakFingerprint()
        {
            return m_environment.ContentFingerprinter.ComputeWeakFingerprint(Process);
        }

        /// <inheritdoc />
        public override bool ShouldHaveArtificialMiss()
        {
            return m_environment.ShouldHaveArtificialMiss(Process);
        }

        /// <inheritdoc />
        public override bool DisableCacheLookup()
        {
            return Process.DisableCacheLookup;
        }

        /// <summary>
        /// Creates a cacheable pip info for a process
        /// </summary>
        public static CacheablePipInfo GetProcessCacheInfo(Process process, PipExecutionContext context)
        {
            return new CacheablePipInfo(
                pip: process,
                context: context,
                outputs: process.FileOutputs,
                dependencies: process.Dependencies,
                directoryOutputs: process.DirectoryOutputs,
                directoryDependencies: process.DirectoryDependencies);
        }
    }
}
