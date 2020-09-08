// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace BuildXL.Plugin
{
    /// <nodoc />
    public interface IPlugin : IPluginClient, IDisposable
    {
        /// <nodoc />
        string FilePath { get; }
        /// <nodoc />
        string Name { get; }
        /// <nodoc />
        string Id { get; }
        /// <nodoc />
        Process PluginProcess { get; }
        /// <nodoc />
        IPluginClient PluginClient { get; }
        /// <nodoc />
        PluginStatus Status { get; set; }
        /// <nodoc />
        Task StartCompletionTask { get; }
        /// <nodoc />
        List<PluginMessageType> SupportedMessageType { get; set; }
    }
}
