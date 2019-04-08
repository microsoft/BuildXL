// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;

namespace VSCode.DebugProtocol
{
    /// <summary>
    /// SetFunctionBreakpoints request; value of the <see cref="IRequest.Command"/> field is "setFunctionBreakpoints".
    ///
    /// Sets multiple function breakpoints and clears all previous function breakpoints.
    /// To clear all function breakpoint, specify an empty array. When a function breakpoint is hit,
    /// a <code cref="IStoppedEvent"/> (reson 'function breakpoint') is generated.
    /// </summary>
    public interface ISetFunctionBreakpointsCommand : ICommand<ISetFunctionBreakpointsResult>
    {
        /// <summary>
        /// The function names of the breakpoints.
        /// </summary>
        IReadOnlyList<IFunctionBreakpoint> Breakpoints { get; }
    }

    /// <summary>
    /// Response to <code cref="ISetFunctionBreakpointsCommand"/> request.
    /// Returned is information about each breakpoint created by this request.
    /// </summary>
    public interface ISetFunctionBreakpointsResult
    {
        /// <summary>
        /// Information about the breakpoints. The array elements correspond to the elements of
        /// the <code cref="ISetFunctionBreakpointsCommand.Breakpoints"/> array.
        /// </summary>
        IReadOnlyList<IBreakpoint> Breakpoints { get; }
    }
}
