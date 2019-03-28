// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace VSCode.DebugProtocol
{
    /// <summary>
    /// Information about a Breakpoint created in <code cref="ISetBreakpointsCommand"/> or
    /// <code cref="ISetFunctionBreakpointsCommand"/>.
    /// </summary>
    public interface IBreakpoint
    {
        /// <summary>
        /// An optional unique identifier for the breakpoint.
        /// </summary>
        int? Id { get; }

        /// <summary>
        /// If true breakpoint could be set (but not necessarily at the desired location).
        /// </summary>
        bool Verified { get; }

        /// <summary>
        /// An optional message about the state of the breakpoint.
        /// This is shown to the user and can be used to explain why a breakpoNumber could not be verified.
        /// </summary>
        string Message { get; }

        /// <summary>
        /// The source where the breakpoint is located.
        /// </summary>
        ISource Source { get; }

        /// <summary>
        /// The actual line of the breakpoint.
        /// </summary>
        int Line { get; }

        /// <summary>
        /// The actual column of the breakpoint.
        /// </summary>
        int? Column { get; }
    }
}
