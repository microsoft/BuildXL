// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;

namespace VSCode.DebugProtocol
{
    /// <summary>
    /// SetBreakpoints request; value of the <see cref="IRequest.Command"/> field is "setBreakpoints".
    ///
    /// Sets multiple breakpoints for a single source and clears all previous breakpoints in that source.
    /// To clear all breakpoint for a source, specify an empty array. When a breakpoint is hit,
    /// a <see cref="IStoppedEvent"/> (reason 'breakpoint') is generated.
    /// </summary>
    public interface ISetBreakpointsCommand : ICommand<ISetBreakpointsResult>
    {
        /// <summary>
        /// The source location of the breakpoints; either source.path or source.reference must be specified.
        /// </summary>
        ISource Source { get; }

        /// <summary>
        /// The code locations of the breakpoints.
        /// </summary>
        IReadOnlyList<ISourceBreakpoint> Breakpoints { get; }
    }

    /// <summary>
    /// Response to <code cref="ISetBreakpointsCommand"/>.
    ///
    /// Returned is information about each breakpoint created by this request.
    /// This includes the actual code location and whether the breakpoint could be verified.
    /// The breakpoints returned are in the same order as the elements of
    /// <code cref="ISetBreakpointsCommand.Breakpoints"/>.
    /// </summary>
    public interface ISetBreakpointsResult
    {
        /// <summary>
        /// Information about the breakpoints. The array elements are in the same order as the elements
        /// of <code cref="ISetBreakpointsCommand.Breakpoints"/>.
        /// </summary>
        IReadOnlyList<IBreakpoint> Breakpoints { get; }
    }
}
