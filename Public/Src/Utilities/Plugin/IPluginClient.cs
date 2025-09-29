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
        /// <summary>
        /// Optional timeout amount (in milliseconds) for gRPC deadline (per request). If not provided,
        /// will default back to <see cref="GrpcPluginSettings.RequestTimeoutInMilliSeconds"/>
        /// </summary>
        int RequestTimeout { get; set; }

        /// <summary>
        /// A list of supported processes by name (only name is supported - no extension or filepath)
        /// </summary>
        HashSet<string> SupportedProcesses { get; set; }

        /// <nodoc />
        Task<PluginResponseResult<bool>> StartAsync();

        /// <nodoc />
        Task<PluginResponseResult<bool>> StopAsync();

        /// <nodoc />
        Task<PluginResponseResult<List<PluginMessageType>>> GetSupportedPluginMessageType();

        /// <nodoc />
        Task<PluginResponseResult<LogParseResult>> ParseLogAsync(string message, bool isErrorStdOutput);

        /// <nodoc />
        Task<PluginResponseResult<ProcessResultMessageResponse>> ProcessResultAsync(string executable,
                                                                                    string arguments,
                                                                                    ProcessStream input,
                                                                                    ProcessStream output,
                                                                                    ProcessStream error,
                                                                                    int exitCode,
                                                                                    string pipSemiStableHash);
    }
}
