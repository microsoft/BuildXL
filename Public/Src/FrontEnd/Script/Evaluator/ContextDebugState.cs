// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using VSCode.DebugProtocol;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.FrontEnd.Script.Evaluator
{
    /// <summary>
    ///     An interface a debugger may implement to track evaluation nodes.
    /// </summary>
    public interface INodeTracker
    {
        /// <summary>
        ///     Decides whether or not to start tracking the <paramref name="node"/>, and returns a boolean denoting
        ///     the decision. If the node is tracked (returned value is <code>true</code>) a corresponding object is
        ///     pushed on the stack (<see cref="Stack"/>).
        /// </summary>
        bool Enter(Node node);

        /// <summary>
        ///     Removes the object from the top of the stack, and asserts that it corresponds to the <paramref name="node"/>.
        /// </summary>
        void Exit(Node node);

        /// <summary>Returns the current stack.</summary>
        Stack Stack { get; }
    }

    /// <summary>
    ///     An interface a debugger may implement and use to store breakpoints associated with the corresponding
    ///     <see cref="Context"/>.  Since a single <see cref="Context"/> is never accessed concurrently, an implementation
    ///     needs not be thread-safe.
    /// </summary>
    public interface IBreakpointStore
    {
        /// <summary>
        ///     Current version of the store.  This version should be incremented after every update made to the store.
        /// </summary>
        int Version { get; }

        /// <summary>
        ///     Whether this store contains any breakpoints.
        /// </summary>
        bool IsEmpty { get; }

        /// <summary>
        ///     Returns a breakpoint matching the current position of the evaluator, or <code>null</code> if
        ///     no breakpoint is found.
        /// </summary>
        /// <remarks>
        ///     The first time we hit a breakpoint we will associate it with the node being evaluated. This way
        ///     we will not pause again for Continue operation (F5 in VS Code) upon hitting any other nodes on
        ///     the same line. The implication is that there is at most one breakpoint at each line.
        /// </remarks>
        IBreakpoint Find(AbsolutePath source, Node node);
    }

    /// <summary>
    ///     In addition to <see cref="IBreakpointStore"/>, must be thread safe and support cloning.
    /// </summary>
    public interface IMasterStore : IBreakpointStore, ICloneable
    {
        /// <summary>
        ///     Sets breakpoints for a given source.  Overwrites any previous breakpoints set for the same source.
        ///     Increments the version.
        /// </summary>
        IReadOnlyList<IBreakpoint> Set(AbsolutePath source, IReadOnlyList<ISourceBreakpoint> breakpoints);

        /// <summary>
        ///     Clears all breakpoints. Increments the version.
        /// </summary>
        void Clear();

        /// <summary>
        ///     Returns a clone of this store which needs not be thread-safe.
        /// </summary>
        new IBreakpointStore Clone();
    }

    /// <summary>
    ///     Represents current debug action, which can be either: 'continue', 'step in', 'step out', 'step over'.
    /// </summary>
    /// <remarks>
    ///     Strongly immutable.
    /// </remarks>
    public sealed class DebugAction
    {
        /// <summary>Different kinds of debug actions.</summary>
        public enum ActionKind
        {
            /// <summary>'Continue' VSCode action (default key binding: F5).</summary>
            Continue,

            /// <summary>'Step In' VSCode action (default key binding: F11).</summary>
            StepIn,

            /// <summary>'Step Out' VSCode action (default key binding: Shift + F11).</summary>
            StepOut,

            /// <summary>'Next' VSCode action (default key binding: F10).</summary>
            StepOver,
        }

        /// <summary>Kind of this debug action.</summary>
        public ActionKind Kind { get; }

        /// <summary>Node at which the debugger was stopped when the action was issued.</summary>
        public Node Node { get; }

        /// <summary>Call stack at the time the debugger stopped.</summary>
        public IReadOnlyList<DisplayStackTraceEntry> CallStack { get; }

        /// <summary>Call stack depth.</summary>
        public int StackSize => CallStack.Count;

        /// <nodoc/>
        public DebugAction(ActionKind kind, Node node, IReadOnlyList<DisplayStackTraceEntry> callStack)
        {
            Contract.Requires(node != null);
            Contract.Requires(callStack != null);

            Kind = kind;
            Node = node;
            CallStack = callStack;
        }
    }

    /// <summary>
    /// <b>Mutable</b> state associated with a <see cref="Context"/> that a debugger may modify during debugging.
    ///
    /// <b>Not thread-safe</b>.  Concurrent modifications of a <see cref="Context"/> are already not allowed,
    /// so this is not an exception.
    /// </summary>
    public sealed class DebugState
    {
        private readonly Queue<Diagnostic> m_errors = new Queue<Diagnostic>();

        /// <summary>Mutable field that a debugger may use to keep track of the current action.</summary>
        public DebugAction Action { get; set; }

        /// <summary>A placeholder for a breakpoint store that a debugger may choose to use.</summary>
        public IBreakpointStore Breakpoints { get; set; }

        /// <summary>A placeholder for a node tracker a debugger may choose to use.</summary>
        public INodeTracker NodeTracker { get; set; }

        /// <summary>
        ///     Remembers an <i>evaluation</i> error so that a debugger may decide to break on it.
        /// </summary>
        /// <remarks>
        ///     Should only be accessed from DScript evaluation threads, which by design have distinct contexts.
        /// </remarks>
        public void AddError(Diagnostic diagnostic)
        {
            m_errors.Enqueue(diagnostic);
        }

        /// <summary>
        ///     Removes the first error and returns its message.
        /// </summary>
        /// <remarks>
        ///     Should only be accessed from DScript evaluation threads, which by design have distinct contexts.
        /// </remarks>
        public string PopLastError()
        {
            return (m_errors.Count == 0) ? null : m_errors.Dequeue().Message;
        }
    }
}
