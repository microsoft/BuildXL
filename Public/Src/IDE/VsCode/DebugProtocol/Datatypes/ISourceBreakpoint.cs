// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace VSCode.DebugProtocol
{
    /// <summary>
    /// Properties of a breakpoint passed to the setBreakpoints request.
    /// </summary>
    public interface ISourceBreakpoint
    {
        /// <summary>
        /// The source line of the breakpoint.
        /// </summary>
        int Line { get; }

        /// <summary>
        /// An optional source column of the breakpoint.
        /// </summary>
        int? Column { get; }

        /// <summary>
        /// An optional expression for conditional breakpoints.
        /// </summary>
        string Condition { get; }
    }
}
