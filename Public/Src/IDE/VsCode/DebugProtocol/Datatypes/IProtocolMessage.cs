// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
