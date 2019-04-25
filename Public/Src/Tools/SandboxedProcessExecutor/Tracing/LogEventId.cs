// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.SandboxedProcessExecutor.Tracing
{
    // disable warning regarding 'missing XML comments on public API'. We don't need docs for these values
#pragma warning disable 1591

    /// <summary>
    /// Defines event IDs corresponding to events in <see cref="Logger" />.
    /// </summary>
    public enum LogEventId : ushort
    {
        // RESERVED TO [8700, 8799] (BuildXL.SandboxedProcessExecutor)
        SandboxedProcessExecutorInvoked = 8700,
        SandboxedProcessExecutorCatastrophicFailure = 8701,
    }
}
