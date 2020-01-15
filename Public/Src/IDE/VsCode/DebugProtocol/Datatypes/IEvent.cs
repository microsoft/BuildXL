// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
