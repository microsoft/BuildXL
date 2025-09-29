// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Plugin.Grpc;
using Grpc.Core;

namespace BuildXL.Plugin
{
    /// <summary>
    /// Plugin serivce client implementations
    /// </summary>
    public class PluginServiceClient : PluginService.PluginServiceClient
    {
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="channel"></param>
        /// <param name="callInvoker"></param>
        public PluginServiceClient(Channel channel, CallInvoker callInvoker) : base(callInvoker) { }

        /// <summary>
        /// send logparse command to remote plugin server
        /// </summary>
        /// <param name="request"></param>
        /// <param name="options"></param>
        /// <returns>plugin response with payload as LogParseMessageResponse <see cref="PluginMessageResponse" /></returns>
        public AsyncUnaryCall<PluginMessageResponse> ParseLogAsync(PluginMessage request, CallOptions options)
        {
            return SendAsync(request, options);
        }

        /// <summary>
        /// send exitcodeparse command to remote plugin server
        /// </summary>
        /// <param name="request"></param>
        /// <param name="options"></param>
        /// <returns>plugin response with payload as ExitCodeParseMessageResponse <see cref="PluginMessageResponse" /></returns>
        public AsyncUnaryCall<PluginMessageResponse> ProcessResultAsync(PluginMessage request, CallOptions options)
        {
            return SendAsync(request, options);
        }
    }
}
