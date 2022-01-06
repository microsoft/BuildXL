// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.Host.Configuration;

#nullable enable

namespace BuildXL.Cache.Host.Service.OutOfProc
{
    /// <summary>
    /// Configuration class used by <see cref="CacheServiceWrapper"/>.
    /// </summary>
    public record class CacheServiceWrapperConfiguration
    {
        /// <nodoc />
        public CacheServiceWrapperConfiguration(
            string serviceId,
            AbsolutePath executable,
            AbsolutePath workingDirectory,
            HostParameters hostParameters,
            AbsolutePath cacheConfigPath,
            AbsolutePath dataRootPath)
        {
            ServiceId = serviceId;
            Executable = executable;
            WorkingDirectory = workingDirectory;
            HostParameters = hostParameters;
            CacheConfigPath = cacheConfigPath;
            DataRootPath = dataRootPath;
        }

        /// <summary>
        /// The identifier used to identify the service for service lifetime management and interruption
        /// </summary>
        public string ServiceId { get; }

        /// <summary>
        /// Path to the executable used when launching the tool relative to the layout root
        /// </summary>
        public AbsolutePath Executable { get; }

        /// <summary>
        /// A working directory used for a process's lifetime tracking and other.
        /// </summary>
        public AbsolutePath WorkingDirectory { get; }

        /// <summary>
        /// Parameters of the running machine like Stamp, Region etc.
        /// </summary>
        public HostParameters HostParameters { get; }

        /// <summary>
        /// A path to the cache configuration (CacheConfiguration.json) file that the child process will use.
        /// </summary>
        public AbsolutePath CacheConfigPath { get; }

        /// <summary>
        /// A root of the data directory (CloudBuild sets this property for the in-proc-mode).
        /// </summary>
        public AbsolutePath DataRootPath { get; }

        /// <summary>
        /// The polling interval of the <see cref="ServiceLifetimeManager"/> used for lifetime tracking of a launched process.
        /// </summary>
        public TimeSpan ServiceLifetimePollingInterval { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// The time to wait for service to shutdown before terminating the process
        /// </summary>
        public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(60);
    }
}
