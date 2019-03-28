// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;

namespace VSCode.DebugProtocol
{
    /// <summary>
    /// Information about the capabilities of a debug adapter.
    /// </summary>
    public interface ICapabilities
    {
        /// <summary>
        /// The debug adapter supports the configurationDoneRequest.
        /// </summary>
        bool SupportsConfigurationDoneRequest { get; }

        /// <summary>
        /// The debug adapter supports function breakpoints (<code cref="IFunctionBreakpoint"/>).
        /// </summary>
        bool SupportsFunctionBreakpoints { get; }

        /// <summary>
        /// The debug adapter supports conditional breakpoints.
        /// </summary>
        bool SupportsConditionalBreakpoints { get; }

        /// <summary>
        /// The debug adapter supports a (side effect free) evaluate request for data hovers.
        /// </summary>
        bool SupportsEvaluateForHovers { get; }

        /// <summary>
        /// Available filters for <code cref="ISetExceptionBreakpointsCommand"/>.
        /// </summary>
        IReadOnlyList<IExceptionBreakpointsFilter> ExceptionBreakpointFilters { get; }

        /// <summary>
        /// The debug adapter supports the completionsRequest.
        /// </summary>
        bool SupportsCompletionsRequest { get; }
    }
}
