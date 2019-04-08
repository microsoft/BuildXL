// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace VSCode.DebugProtocol
{
    /// <summary>
    /// An ExceptionBreakpointsFilter is shown in the UI as an option for configuring how exceptions are dealt with.
    /// </summary>
    public interface IExceptionBreakpointsFilter
    {
        /// <summary>
        /// The internal ID of the filter. This value is passed to the <code cref="ISetExceptionBreakpointsCommand"/>.
        /// </summary>
        string Filter { get; }

        /// <summary>
        /// The name of the filter. This will be shown in the UI.
        /// </summary>
        string Label { get; }

        /// <summary>
        /// Initial value of the filter. If not specified a value 'false' is assumed.
        /// </summary>
        bool Default { get; }
    }
}
