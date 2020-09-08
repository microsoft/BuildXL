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
    }
}
