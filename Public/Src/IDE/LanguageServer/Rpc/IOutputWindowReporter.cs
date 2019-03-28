// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if FEATURE_MICROSOFT_DIAGNOSTICS_TRACING
using Microsoft.Diagnostics.Tracing;
#else
using System.Diagnostics.Tracing;
#endif

namespace BuildXL.Ide.JsonRpc
{
    /// <summary>
    /// Interface for writing messages to the IDE's output window.
    /// </summary>
    public interface IOutputWindowReporter
    {
        /// <nodoc />
        void WriteLine(EventLevel level, string message);
    }
}
