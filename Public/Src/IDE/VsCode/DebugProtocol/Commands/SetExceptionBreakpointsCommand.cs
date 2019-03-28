// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace VSCode.DebugProtocol
{
    /// <summary>
    /// SetExceptionBreakpoints request; value of the <see cref="IRequest.Command"/> field is "setExceptionBreakpoints".
    ///
    /// Enable that the debuggee stops on exceptions with a <cod cref="IStoppedEvent"/> (reason 'exception').
    ///
    /// A response to this request is just an acknowledgement, so no body field is required.
    /// </summary>
    public interface ISetExceptionBreakpointsCommand : ICommand<ISetExceptionBreakpointsResult>
    {
        /// <summary>
        /// Names of enabled exception breakpoints.
        /// </summary>
        IReadOnlyList<string> Filters { get; }
    }

    /// <summary>
    /// Response to <see cref="ISetExceptionBreakpointsCommand"/>
    /// </summary>
    [SuppressMessage("Microsoft.Design", "CA1040:EmptyInterface")]
    public interface ISetExceptionBreakpointsResult { }
}
