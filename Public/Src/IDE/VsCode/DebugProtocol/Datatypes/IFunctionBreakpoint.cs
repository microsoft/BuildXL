// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
