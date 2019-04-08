// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace VSCode.DebugProtocol
{
    /// <summary>
    /// Event message for "initialized" event type.
    ///
    /// This event indicates that the debug adapter is ready to accept configuration requests
    /// (e.g., <code cref="ISetBreakpointsCommand"/>, <code cref="ISetExceptionBreakpointsCommand"/>, etc.).
    ///
    /// A debug adapter is expected to send this event when it is ready to accept configuration requests
    /// (but not before the <code cref="IInitializeCommand"/> has finished).
    ///
    /// The sequence of events/requests is as follows:
    ///   - adapter sends <code cref="IInitializedEvent"/> (after the <code cref="IInitializeCommand"/> has returned)
    ///   - frontend sends zero or more <code cref="ISetBreakpointsCommand"/>
    ///   - frontend sends one <code cref="ISetFunctionBreakpointsCommand"/>
    ///   - frontend sends a <code cref="ISetExceptionBreakpointsCommand"/> if one or more
    ///     <code cref="ICapabilities.ExceptionBreakpointFilters"/> have been defined
    ///     (or if <code cref="ICapabilities.SupportsConfigurationDoneRequest"/> is not defined or false)
    ///   - frontend sends other future configuration requests
    ///   - frontend sends one <code cref="IConfigurationDoneCommand"/> to indicate the end of the configuration.
    /// </summary>
    public interface IInitializedEvent : IEvent<object> { }
}
