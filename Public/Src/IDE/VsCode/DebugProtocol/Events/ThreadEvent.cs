// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace VSCode.DebugProtocol
{
    /// <summary>
    /// Event message for "thread" event type.
    /// The event indicates that a thread has started or exited.
    /// </summary>
    public interface IThreadEvent : IEvent<IThreadEventBody> { }

    /// <summary>
    /// Body for <code cref="IThreadEvent"/>
    /// </summary>
    public interface IThreadEventBody
    {
        /// <summary>
        /// The reason for the event (such as: 'started', 'exited').
        /// </summary>
        string Reason { get; }

        /// <summary>
        /// The identifier of the thread.
        /// </summary>
        int ThreadId { get; }
    }
}
