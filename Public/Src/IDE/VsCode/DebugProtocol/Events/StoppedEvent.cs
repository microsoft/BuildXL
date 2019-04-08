// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace VSCode.DebugProtocol
{
    /// <summary>
    /// Event message for "stopped" event type.
    /// The event indicates that the execution of the debuggee has stopped due to some condition.
    /// This can be caused by a breakpoint previously set, a stepping action has completed, by executing a debugger statement etc.
    /// </summary>
    public interface IStoppedEvent : IEvent<IStoppedEventBody> { }

    /// <summary>
    /// Body for <code cref="IStoppedEvent"/>
    /// </summary>
    public interface IStoppedEventBody
    {
        /// <summary>
        /// The reason for the event (such as: 'step', 'breakpoint', 'exception', 'pause'). This string is shown in the UI.
        /// </summary>
        string Reason { get; }

        /// <summary>
        /// The thread which was stopped.
        /// </summary>
        int ThreadId { get; }

        /// <summary>
        /// Additional information. E.g., if reason is 'exception', text contains the exception name. This string is shown in the UI.
        /// </summary>
        string Text { get; }

        /// <summary>
        /// If <code cref="AllThreadsStopped"/> is true, a debug adapter can announce that all threads have stopped.
        /// The client should use this information to enable that all threads can be expanded to access their stacktraces.
        /// If the attribute is missing or false, only the thread with the given <code cref="ThreadId"/> can be expanded.
        /// </summary>
        bool AllThreadsStopped { get; }
    }
}
