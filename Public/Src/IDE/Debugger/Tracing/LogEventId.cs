// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.FrontEnd.Script.Debugger.Tracing
{
    // disable warning regarding 'missing XML comments on public API'. We don't need docs for these values
#pragma warning disable 1591

    /// <summary>
    /// Defines event IDs corresponding to events in <see cref="Logger" />
    /// <remarks>
    /// We have three main sections for errors
    /// 1) Syntactic errors:
    ///    a) Errors that come from the ported typescript parser. Error code is computed as original error code + a base number
    ///    b) Errors that are found by mandatory lint rules
    ///    c) Errors that are found by policy lint rules
    /// 2) AstConverter errors. Semantics errors found during conversion (e.g. double declaration)
    /// 3) Evaluation phase errors
    ///
    /// Each section, and subsection, have its own range
    ///
    /// Assembly reserved range 9900 - 9999
    /// </remarks>
    /// </summary>
    public enum LogEventId : ushort
    {
        None = 0,

        DebuggerServerStarted = 9900,
        DebuggerServerShutDown = 9901,
        DebuggerClientConnected = 9902,
        DebuggerClientDisconnected = 9903,
        DebuggerRequestReceived = 9904,
        DebuggerMessageSent = 9905,
        DebuggerEvaluationThreadSuspended = 9906,
        DebuggerEvaluationThreadResumed = 9907,

        // errors treated as warnings
        DebuggerCannotOpenSocketError = 9908,
        DebuggerServerGenericError = 9909,
        DebuggerClientGenericError = 9910,

        // warnings
        DebuggerRendererFailedToBindCallableMember = 9911,
    }
}
