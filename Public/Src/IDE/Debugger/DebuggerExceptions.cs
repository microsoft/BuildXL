// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.FrontEnd.Script.Debugger
{
    /// <summary>
    /// Used only to handle the rare cases in which a client debugger requests a state that is missing,
    /// or has become obsolete (e.g., requests invalid thread id, invalid frame index, etc.).
    ///
    /// In theory, these exceptions should never happen.  To be on the safe side, <see cref="RemoteDebugger"/>
    /// in its top-level <see cref="RemoteDebugger.DispatchRequest(VSCode.DebugProtocol.IRequest)"/>,
    /// method catches these exception and sends an error response to the client debugger.
    /// </summary>
    [SuppressMessage("Microsoft.Usage", "CA2237:Add [serializable]", Justification = "Not serializable")]
    public abstract class DebuggerException : Exception
    {
        /// <nodoc/>
        protected DebuggerException(string message)
            : base(message) { }
    }

    /// <summary>
    /// Used only to handle the rare case in which a client debugger requests something for a
    /// thread that is not found in this debugger's local state of stopped threads.
    ///
    /// This should never happen.  To be on the safe side, however, a special method (<see cref="DebuggerState.GetThreadState"/>)
    /// should always used to retrieve thread state, which in turn checks if a state for a given thread ID is found;
    /// if not found, it throws this exception, which is then caught by <see cref="RemoteDebugger"/>.
    /// </summary>
    [SuppressMessage("Microsoft.Design", "CA1032: Add the following constructor...", Justification = "No need for such constructors")]
    public sealed class ThreadNotFoundException : DebuggerException
    {
        private const string DefaultMessage = "Evaluation state for thread '{0}' not found (the thread might not be stopped anymore).";

        /// <nodoc/>
        public ThreadNotFoundException(int threadId)
            : base(string.Format(CultureInfo.InvariantCulture, DefaultMessage, threadId)) { }
    }

    /// <summary>
    /// Used only to handle the rare case when a client debugger requests a frame whose index is out of bounds.
    ///
    /// This should never happen.  To be on the safe side, however, a special method (<see cref="EvaluationState.GetDisplayStackEntryForFrame"/>)
    /// should always used to retrieve the stack entry, which in turn checks if the index is within bounds;
    /// if it is not, it throws this exception, which is then caught by <see cref="RemoteDebugger"/>.
    /// </summary>
    [SuppressMessage("Microsoft.Design", "CA1032: Add the following constructor...", Justification = "No need for such constructors")]
    public sealed class InvalidFrameIndexException : DebuggerException
    {
        private const string DefaultMessage = "Frame index for thread '{0}' out of range: index: {1}, call stack size: {2}.";

        /// <nodoc/>
        public InvalidFrameIndexException(int threadId, int frameIndex, int callStackSize)
            : base(string.Format(CultureInfo.InvariantCulture, DefaultMessage, threadId, frameIndex, callStackSize)) { }
    }
}
