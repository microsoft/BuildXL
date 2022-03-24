// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Utilities.Collections;

#nullable enable

namespace BuildXL.Cache.Host.Service.OutOfProc
{
    /// <summary>
    /// Configuration class used by <see cref="CacheServiceWrapper"/>.
    /// </summary>
    public record class CacheServiceWrapperConfiguration
    {
        /// <summary>
        /// The service id used by <see cref="ServiceLifetimeManager"/> for tracking the lifetime of a launched CaSaaS process.
        /// </summary>
        public const string CacheServiceId = "OutOfProcCache";

        /// <nodoc />
        public CacheServiceWrapperConfiguration(
            string serviceId,
            AbsolutePath executable,
            AbsolutePath workingDirectory,
            HostParameters hostParameters,
            AbsolutePath cacheConfigPath,
            AbsolutePath dataRootPath,
            bool useInterProcSecretsCommunication,
            IReadOnlyDictionary<string, string>? environmentVariables)
        {
            ServiceId = serviceId;
            Executable = executable;
            WorkingDirectory = workingDirectory;
            HostParameters = hostParameters;
            CacheConfigPath = cacheConfigPath;
            DataRootPath = dataRootPath;
            UseInterProcSecretsCommunication = useInterProcSecretsCommunication;
            EnvironmentVariables = environmentVariables ?? CollectionUtilities.EmptyDictionary<string, string>();
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

        /// <summary>
        /// If true, then memory-mapped-based secrets communication is used.
        /// </summary>
        public bool UseInterProcSecretsCommunication { get; }

        /// <summary>
        /// Additional environment variables that will be set for the child process.
        /// </summary>
        public IReadOnlyDictionary<string, string> EnvironmentVariables { get; }

        /// <summary>
        /// Gets default environment variables passed to the child process by default.
        /// </summary>
        /// <returns></returns>
        public static IReadOnlyDictionary<string, string> DefaultDotNetEnvironmentVariables =>
            new Dictionary<string, string>
                   {
                       ["COMPlus_GCCpuGroup"] = "1",
                       ["DOTNET_GCCpuGroup"] = "1", // This is the same option that is used by .net6+
                       ["COMPlus_Thread_UseAllCpuGroups"] = "1",
                       ["DOTNET_Thread_UseAllCpuGroups"] = "1", // This is the same option that is used by .net6+
                   };
    }
}
