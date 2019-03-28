// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace VSCode.DebugProtocol
{
    /// <summary>
    /// Server-initiated event.
    /// </summary>
    public interface IEvent<T> : IProtocolMessage
    {
        /// <summary>
        /// Type of event.
        /// </summary>
        string EventType { get; }

        /// <summary>
        /// Event-specific information.
        /// </summary>
        T Body { get; }
    }
}
