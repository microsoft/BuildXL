// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.Tracing;

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
