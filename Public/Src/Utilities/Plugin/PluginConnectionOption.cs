// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Core.Logging;

namespace BuildXL.Plugin
{
    /// <nodoc />
    public class PluginConnectionOption
    {
        /// <nodoc />
        public string LogDir;
        /// <nodoc />
        public bool LogVerbose;
        /// <nodoc />
        public string IpcMoniker;
        /// <nodoc />
        public ILogger Logger;
        /// <summary>
        /// How often (in seconds) the client sends HTTP/2 PING frames.
        /// </summary>
        public int KeepAlivePingDelayInSeconds = GrpcPluginSettings.KeepAlivePingDelayInSeconds;
        /// <summary>
        /// How long (in seconds) to wait for PING acknowledgement.
        /// </summary>
        public int KeepAlivePingTimeoutInSeconds = GrpcPluginSettings.KeepAlivePingTimeoutInSeconds;
    }
}
