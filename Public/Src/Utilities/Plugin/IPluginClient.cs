// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Plugin.Grpc;

namespace BuildXL.Plugin
{
    /// <nodoc />
    public interface IPluginClient: IDisposable
    {
        /// <nodoc />
        Task<PluginResponseResult<bool>> StartAsync();

        /// <nodoc />
        Task<PluginResponseResult<bool>> StopAsync();

        /// <nodoc />
        Task<PluginResponseResult<List<PluginMessageType>>> GetSupportedPluginMessageType();

        /// <nodoc />
        Task<PluginResponseResult<LogParseResult>> ParseLogAsync(string message, bool isErrorStdOutput);
    }
}
