// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace VSCode.DebugProtocol
{
    /// <summary>
    /// Server-initiated response to client request.
    /// </summary>
    public interface IResponse : IProtocolMessage
    {
        /// <summary>
        /// Sequence number of the corresponding request.
        /// </summary>
        int RequestSeq { get; }

        /// <summary>
        /// Outcome of the request.
        /// </summary>
        bool Success { get; }

        /// <summary>
        /// The command requested.
        /// </summary>
        string Command { get; }

        /// <summary>
        /// Contains error message if success == false.
        /// </summary>
        string Message { get; }

        /// <summary>
        /// Contains request result if success is true and optional error details if success is false.
        /// </summary>
        object Body { get; }
    }
}
