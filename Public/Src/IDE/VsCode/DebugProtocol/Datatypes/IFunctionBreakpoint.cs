// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace VSCode.DebugProtocol
{
    /// <summary>
    /// Properties of a breakpoint passed to <code cref="ISetFunctionBreakpointsCommand"/>.
    /// </summary>
    public interface IFunctionBreakpoint
    {
        /// <summary>
        /// The name of the function.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// An optional expression for conditional breakpoints.
        /// </summary>
        string Condition { get; }
    }
}
