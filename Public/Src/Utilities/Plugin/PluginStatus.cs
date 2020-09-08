// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Plugin
{
    /// <summary>
    /// Plugin status
    /// </summary>
    public enum PluginStatus : byte
    {
        /// <nodoc />
        Unknown,
        /// <nodoc />
        Initialized,
        /// <nodoc/>
        Running,
        /// <nodoc />
        Stopped,
        /// <nodoc />
        Faulted
    }
}
