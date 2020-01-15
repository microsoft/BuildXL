// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace VSCode.DebugProtocol
{
    /// <summary>
    /// Event message for "breakpoint" event type.
    /// The event indicates that some information about a breakpoint has changed.
    /// </summary>
    public interface IBreakpointEvent : IEvent<IBreakpointEventBody> { }

    /// <summary>
    /// Body for <code cref="IBreakpointEvent"/>.
    /// </summary>
    public interface IBreakpointEventBody
    {
        /// <summary>
        /// The reason for the event (such as: 'changed', 'new').
        /// </summary>
        string Reason { get; }

        /// <summary>
        /// The breakpoint.
        /// </summary>
        IBreakpoint Breakpoint { get; }
    }
}
