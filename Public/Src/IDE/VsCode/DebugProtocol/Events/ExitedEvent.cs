// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace VSCode.DebugProtocol
{
    /// <summary>
    /// Event message for "exited" event type.
    /// The event indicates that the debuggee has exited.
    /// </summary>
    public interface IExitedEvent : IEvent<IExitedEventBody> { }

    /// <summary>
    /// Body for <code cref="IExitedEvent"/>.
    /// </summary>
    public interface IExitedEventBody
    {
        /// <summary>
        /// The exit code returned from the debuggee.
        /// </summary>
        int ExitCode { get; }
    }
}
