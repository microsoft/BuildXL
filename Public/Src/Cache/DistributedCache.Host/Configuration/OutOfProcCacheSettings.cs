// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Cache.Host.Configuration
{
#nullable enable

    public class OutOfProcCacheSettings
    {
        /// <summary>
        /// A path to the cache configuration that the launched process will use.
        /// </summary>
        /// <remarks>
        /// This property needs to be set in CloudBuild in order to use 'out-of-proc' cache.
        /// </remarks>
        public string? CacheConfigPath { get; set; }

        /// <summary>
        /// A relative path from the current executing assembly to the stand-alone cache service that will be launched in a separate process.
        /// </summary>
        public string? Executable { get; set; }

        public TimeSpanSetting? ServiceLifetimePollingInterval { get; set; }

        public TimeSpanSetting? GracefulShutdownTimeout { get; set; }

        public TimeSpanSetting? KillTimeout { get; set; }

        /// <summary>
        /// If true, then memory-mapped-based secrets communication is used.
        /// </summary>
        public bool UseInterProcSecretsCommunication { get; set; } = false;

        /// <summary>
        /// An optional file name used for inter-process secrets communication.
        /// </summary>
        public string? InterProcessSecretsCommunicationFileName { get; set; }

        /// <summary>
        /// Additional environment variables passed to the launched out-of-proc CaSaaS.
        /// </summary>
        public Dictionary<string, string> EnvironmentVariables { get; set; } = new Dictionary<string, string>();

    }
}
