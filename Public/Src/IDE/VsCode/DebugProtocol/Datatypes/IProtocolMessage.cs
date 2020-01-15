// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace VSCode.DebugProtocol
{
    /// <summary>
    /// Base class of request, responses, end events.
    /// </summary>
    public interface IProtocolMessage
    {
        /// <summary>
        /// Sequence number.
        /// </summary>
        int Seq { get; set; }

        /// <summary>
        /// One of "request", "response", or "event".
        /// </summary>
        string Type { get; }
    }
}
