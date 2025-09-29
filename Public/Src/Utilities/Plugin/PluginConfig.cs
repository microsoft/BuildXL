// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Plugin
{
    /// <summary>
    /// When loading a plugin via the /pluginPaths arg, a plugin can be provided directly (.exe) or via config file (.config).
    /// This class defines the JSON structure for a config file
    /// </summary>
    public class PluginConfig
    {
        /// <summary>
        /// The absolute path to the BuildXL plugin .exe
        /// </summary>
        public string PluginPath { get; set; }

        /// <summary>
        /// Optional timeout amount (in milliseconds) for gRPC deadline (per request). If not provided,
        /// will default back to <see cref="GrpcPluginSettings.RequestTimeoutInMilliSeconds"/>
        /// </summary>
        public int Timeout { get; set; }

        /// <summary>
        /// A list of supported processes by name (only name is supported - no extension or filepath)
        /// </summary>
        public List<string> SupportedProcesses { get; set; }

        /// <summary>
        /// A list of the message types that the plugin can support <see cref="PluginMessageType"/>
        /// </summary>
        public List<PluginMessageType> MessageTypes { get; set; }
    }
}
