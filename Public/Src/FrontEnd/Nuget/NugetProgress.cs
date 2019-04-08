// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities.Configuration;

namespace BuildXL.FrontEnd.Nuget
{
    /// <summary>
    /// Tracks progress information for nuget packages.
    /// </summary>
    /// <remarks>
    /// The type is thread safe.
    /// The thread safety is achieved by using 'lock(this)' in all the public methods. This is considered as a bad practice, but in this case there is no issues with this approach
    /// because the instance should not be used as a sync root in the system.
    /// </remarks>
    public sealed class NugetProgress
    {
        private readonly Stopwatch m_stopwatch;

        /// <nodoc />
        public NugetProgressState State { get; private set; }

        /// <nodoc />
        public TimeSpan StartTime { get; private set; }

        /// <nodoc />
        public TimeSpan EndTime { get; private set; }

        /// <nodoc />
        public INugetPackage Package { get; }

        /// <nodoc />
        public NugetProgress(INugetPackage package, Stopwatch stopwatch)
        {
            Contract.Requires(package != null);
            Contract.Requires(stopwatch != null);

            Package = package;
            m_stopwatch = stopwatch;
        }

        /// <summary>
        /// Record start of processing
        /// </summary>
        public void StartRunning()
        {
            lock (this)
            {
                State = NugetProgressState.Running;
                StartTime = m_stopwatch.Elapsed;
            }
        }

        /// <summary>
        /// Record start of download
        /// </summary>
        public void StartDownloadFromNuget()
        {
            lock (this)
            {
                State = NugetProgressState.DownloadingFromNuget;
                StartTime = m_stopwatch.Elapsed;
            }
        }

        /// <summary>
        /// Record completion of download
        /// </summary>
        public void CompleteDownload()
        {
            lock (this)
            {
                State = NugetProgressState.Succeeded;
                EndTime = m_stopwatch.Elapsed;
            }
        }

        /// <summary>
        /// Record failure of download
        /// </summary>
        public void FailedDownload()
        {
            lock (this)
            {
                State = NugetProgressState.Failed;
                EndTime = m_stopwatch.Elapsed;
            }
        }

        /// <summary>
        /// Computes the time elapsed when running
        /// </summary>
        public TimeSpan Elapsed()
        {
            // The instance is used in a multithreaded fasion, so it is possible that this method is called
            // when the state is already one of the completed ones.
            Contract.Requires(State != NugetProgressState.Waiting);
            
            lock (this)
            {
                return m_stopwatch.Elapsed - StartTime;
            }
        }
    }
}
