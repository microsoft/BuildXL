// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
#pragma warning disable 1591 // disabling warning about missing API documentation; TODO: Remove this line and write documentation!

namespace VSCode.DebugProtocol
{
    /// <summary>
    /// Client-initiated request.
    /// </summary>
    public interface IRequest : IProtocolMessage
    {
        /// <summary>
        /// The command to execute.
        /// </summary>
        string Command { get; }

        /// <summary>
        /// Object containing arguments for the command.
        /// </summary>
        object Arguments { get; }
    }

    public interface ICommand<TResult>
    {
        void SendResult(TResult result);

        void SendErrorResult(int id, string message, bool showUser = true, bool sendTelemetry = false, string url = null, string urlLabel = null);
    }
}
