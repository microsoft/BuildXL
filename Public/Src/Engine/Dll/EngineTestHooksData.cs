// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Engine.Cache;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Scheduler;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Engine
{
    /// <summary>
    /// Class that contains hooks for test to inspect the internal state
    /// while not having to hold on to the state after it is needed in regular executions
    /// </summary>
    public sealed class EngineTestHooksData : IDisposable
    {
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="captureScheduler">indicates the scheduler should be captured and not disposed with engine.</param>
        /// <param name="captureFrontEndEngineAbstraction">indicates the created FrontEndEngineAbstraction should be captured and not disposed with engine.</param>
        public EngineTestHooksData(bool captureScheduler = false, bool captureFrontEndEngineAbstraction = false)
        {
            if (captureScheduler)
            {
                Scheduler = new BoxRef<Scheduler.Scheduler>();
            }

            if (captureFrontEndEngineAbstraction)
            {
                FrontEndEngineAbstraction = new BoxRef<FrontEndEngineAbstraction>();
            }
        }

        /// <summary>
        /// Specifies the factory for creating the cache to use.
        /// </summary>
        public Func<EngineCache> CacheFactory { get; set; }

        /// <summary>
        /// The scheduler used by the Engine
        /// </summary>
        public BoxRef<Scheduler.Scheduler> Scheduler { get; set; }

        /// <summary>
        /// The FrontEndEngineAbstraction created by the Engine
        /// </summary>
        public BoxRef<FrontEndEngineAbstraction> FrontEndEngineAbstraction { get; set; }

        /// <summary>
        /// Salt for graph fingerprint.
        /// </summary>
        public int? GraphFingerprintSalt { get; set; } = null;

        /// <summary>
        /// The AppDeployment used for an identity in graph caching
        /// </summary>
        public AppDeployment AppDeployment { get; set; } = null;

        /// <summary>
        /// Result of graph reuse check.
        /// </summary>
        public GraphReuseResult GraphReuseResult { get; set; } = null;

        /// <summary>
        /// Whether BuildXL should warn about directories that have virus scanned enabled
        /// </summary>
        public bool DoWarnForVirusScan { get; set; } = true;

        /// <summary>
        /// The temp directory the Engine used for initializing its <see cref="TempCleaner"/>
        /// The <see cref="TempCleaner"/>'s temp directory is passed into 
        /// <see cref="BuildXL.Native.IO.FileUtilities.DeleteFile(string, bool, BuildXL.Native.IO.ITempDirectoryCleaner)"/>
        /// for move-deleting files
        /// </summary>
        public string TempCleanerTempDirectory { get; set; } = null;

        /// <inheritdoc />
        public void Dispose()
        {
            Scheduler?.Value?.PipGraph.PipTable.Dispose();
        }
    }
}
