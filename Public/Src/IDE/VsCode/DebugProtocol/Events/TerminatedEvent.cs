// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace VSCode.DebugProtocol
{
    /// <summary>
    /// Event message for "terminated" event type.
    /// The event indicates that debugging of the debuggee has terminated.
    /// </summary>
    public interface ITerminatedEvent : IEvent<ITerminatedEventBody> { }

    /// <summary>
    /// Body for <code cref="ITerminatedEvent"/>
    /// </summary>
    public interface ITerminatedEventBody
    {
        /// <summary>
        /// A debug adapter may set 'restart' to true to request that the front end restarts the session.
        /// </summary>
        bool Restart { get; }
    }
}
