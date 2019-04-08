// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Statements;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using VSCode.DebugAdapter;
using VSCode.DebugProtocol;

namespace BuildXL.FrontEnd.Script.Debugger
{
    /// <summary>
    /// <para>
    ///     A manager class responsible for driving the DScript node evaluation in the
    ///     presence of the breakpoints and other debugging features.
    /// </para>
    /// <para>
    ///     The <code cref="NodeEval"/> method can be used as a decorator around <code cref="Node.Eval"/>
    ///     to "bolt on" this debugger on top of the standard DScript evaluation.
    /// </para>
    /// </summary>
    public sealed class NodeEvalManager
    {
        private const string PauseReasonBreakpoint = "breakpoint";

        private readonly DebuggerState m_state;
        private readonly IDebugger m_debugger;

        /// <summary>
        ///     Constructor.
        /// </summary>
        ///
        /// <param name="state">
        ///     Shared debugger state.  Must be thread-safe, since it will be accessed
        ///     by this class from multiple DScript evaluation threads (tasks).
        /// </param>
        /// <param name="debugger">
        ///     Debugger to notify when a thread is stopped
        /// </param>
        public NodeEvalManager(DebuggerState state, IDebugger debugger)
        {
            Contract.Requires(state != null);
            Contract.Requires(debugger != null);

            m_state = state;
            m_debugger = debugger;
        }

        /// <summary>
        /// <para>
        ///     Implements the <code cref="IDecorator{T}"/> delegate type.
        /// </para>
        /// <para>
        ///     Implements debugging features around DScript node evaluation.
        ///     At the beginnign of each call, checks if there exists a breakpoint
        ///     at the location of <paramref name="node"/>.  If it does, it saves
        ///     the evaluation state, updates the debugger state (add the current
        ///     thread to its list of stopped threads), and blocks the caller thread.
        /// </para>
        /// <para>
        ///     When the caller thread is unblocked (e.g., by receiving the
        ///     <code cref="DebugSession.Continue"/> request from the
        ///     client debugger), the execution proceeds to the original continuation
        ///     (e.g., <code cref="Node.Eval"/>) and its result is returned.
        /// </para>
        /// </summary>
        public EvaluationResult NodeEval(Node node, Context context, ModuleLiteral env, EvaluationStackFrame args, Func<EvaluationResult> continuation)
        {
            if (m_state.DebuggerStopped || IsIllegalToBreakOnSyntaxKind(node.Kind))
            {
                return continuation();
            }

            // Enter compound statement
            var statementTracker = GetStatementTracker(context);
            bool tracking = statementTracker.Enter(node);

            try
            {
                PreEvalUpdateLocalVarNames(node, context);
                var ans = (context.DebugState.Action != null)
                    ? StepThrough(node, context, env, args, continuation)
                    : RunToBreakpoint(node, context, env, args, continuation);
                PostEvalUpdateLocalVarNames(node, context);

                string errorIfAny = context.DebugState.PopLastError();
                if (errorIfAny != null)
                {
                    PauseEvaluation(node, context, env, args, "exception", errorIfAny);
                }

                // Always pause at the call site after exiting the function.
                // This applies to StepOver, StepIn and StepOut, but not Continue (F5).
                var debugAction = context.DebugState.Action;
                if (IsApplyExpressionNode(node) &&
                    IsSteppingAction(debugAction) &&
                    IsCallStackShallower(node, context, env, debugAction))
                {
                    PauseEvaluation(node, context, env, args, GetPauseReason(debugAction));
                }

                return ans;
            }
            finally
            {
                // Exit strucutual statement
                if (tracking)
                {
                    statementTracker.Exit(node);
                }
            }
        }

        internal static bool IsIllegalToBreakOnSyntaxKind(SyntaxKind kind)
        {
            switch (kind)
            {
                case SyntaxKind.LocalReferenceExpression:
                case SyntaxKind.SelectorExpression:
                case SyntaxKind.ArrayLiteral:
                case SyntaxKind.ObjectLiteralSlim:
                case SyntaxKind.ObjectLiteralN:
                case SyntaxKind.StringLiteral:
                case SyntaxKind.NumberLiteral:
                case SyntaxKind.BlockStatement:
                case SyntaxKind.LambdaExpression:
                    return true;
                default:
                    return false;
            }
        }

        private EvaluationResult RunToBreakpoint(Node node, Context context, ModuleLiteral env, EvaluationStackFrame args, Func<EvaluationResult> continuation)
        {
            IBreakpointStore breakpoints = GetBreakpointStore(context);
            if (breakpoints.IsEmpty)
            {
                return continuation();
            }

            IBreakpoint atBreakpoint = breakpoints.Find(env.Path, node);

            // if at a breakpoint:
            if (atBreakpoint != null)
            {
                PauseEvaluation(node, context, env, args, PauseReasonBreakpoint);
            }

            return continuation();
        }

        private EvaluationResult StepThrough(Node node, Context context, ModuleLiteral env, EvaluationStackFrame args, Func<EvaluationResult> continuation)
        {
            Contract.Requires(context?.DebugState?.Action != null);

            var previousStop = context.DebugState.Action;
            var callStack = EvaluationState.GetStackTrace(context, env, node);
            var callStackSize = callStack.Length;

            // dont't stop if either:
            //   (1) no call stack is available, or
            //   (2) top stack is an ambient call (because there are no sources for ambient calls), or
            //   (3) current node is of a certain type (because people are not used to it)
            if (callStackSize == 0 ||
                DoesTopStackHaveSource(callStack) ||
                (node is FunctionLikeExpression) ||
                (node is BlockStatement))
            {
                return RunToBreakpoint(node, context, env, args, continuation);
            }

            if (ShouldPause(node, context, previousStop, callStackSize))
            {
                PauseEvaluation(node, context, env, args, GetPauseReason(previousStop));
            }

            return RunToBreakpoint(node, context, env, args, continuation);
        }

        /// <summary>
        ///     Check if we should pause in reaction to the given action.
        /// </summary>
        /// <remarks>
        ///     When the debug command is "StepOver" (F10 in VS Code), we must step over to the next logically reasonable place, typically next statement, and pause there.
        ///     When the debug command is "StepIn" (F11 in VS Code), we must pause if the current statement causes a function call; otherwise default to StepOver behavior.
        /// </remarks>
        /// <param name="node">The node this method is to determine if we should step over.</param>
        /// <param name="context">The evaluation context.</param>
        /// <param name="prevState">The previous debug state.</param>
        /// <param name="callStackSize">The current call stack size.</param>
        /// <returns>
        ///     <code>false</code> to step over <paramref name="node"/>; <code>true</code> to pause at <paramref name="node"/>.
        /// </returns>
        private static bool ShouldPause(Node node, Context context, DebugAction prevState, int callStackSize)
        {
            // 1) Do not pause if it's neither StepIn nor StepOver
            bool isStepIn = false;
            if (prevState.Kind == DebugAction.ActionKind.StepIn)
            {
                isStepIn = true;
            }
            else if (prevState.Kind != DebugAction.ActionKind.StepOver)
            {
                return false;
            }

            // 2) If stack has shrinked, pause unconditionally
            if (callStackSize < prevState.StackSize)
            {
                return true;
            }

            // 3) If stack has grown
            if (callStackSize > prevState.StackSize)
            {
                // Pause if stepping in. We are in a new frame; but always step over any function call.
                return isStepIn;
            }

            // 4) Try handle this with regards to the enclosing structural statement
            // This is to override the default behavior where we always pause at a statement and always skip over a non-statement - see 4)
            var statementTracker = GetStatementTracker(context);
            bool? shouldPause = CompoundStatementTracker.ShouldPauseWhenSteppingOver(statementTracker.Stack, node);
            if (shouldPause.HasValue)
            {
                return shouldPause.Value;
            }

            // 5) Pause if this is a statement; skip over if not.
            return node is Statement;
        }

        internal void PauseEvaluation(Node node, Context context, ModuleLiteral env, EvaluationStackFrame args, string reason, string message = null)
        {
            // don't stop if debugger has disconnected.
            if (m_state.DebuggerStopped)
            {
                return;
            }

            var threadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            var evalState = new EvaluationState(threadId, node, context, env, args);

            WriteToDebuggerConsole(string.Format(
                CultureInfo.InvariantCulture,
                "[{0}] stopped at ({1}, line: {2}, col: {3}) {4}\n",
                reason,
                node.GetType().Name,
                node.Location.Line,
                node.Location.Position,
                node.ToDisplayString(context)));
            PauseThread(evalState, reason, message);

            // We may pause at a line with a breakpoint which has not been hit so far. If we pause
            // by Continue hitting this breakpoint, it's just fine; but if we pause due to stepping,
            // we need immediately associate the current code with this breakpoint to avoid pausing
            // again at the same line.
            if (reason != PauseReasonBreakpoint)
            {
                GetBreakpointStore(context).Find(env.Path, node);
            }
        }

        internal void PauseHost(IEnumerable<IModuleAndContext> evaluatedModules)
        {
            if (m_state.DebuggerStopped)
            {
                return;
            }

            var threadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            PauseThread(new DScriptHostState(threadId, evaluatedModules), "EOE");
        }

        /// <summary>
        ///     1. save evaluation state
        ///     2. send "StoppedEvent" to the front end debugger
        ///     3. block current thread
        /// </summary>
        private void PauseThread(ThreadState threadState, string reason, string message = null)
        {
            var threadId = threadState.ThreadId;
            m_state.SetThreadState(threadState);
            NotifyDebuggerThreadStopped(threadId, reason, message);
            m_state.Logger.ReportDebuggerEvaluationThreadSuspended(m_state.LoggingContext, threadId);
            threadState.PauseEvaluation();
            m_state.Logger.ReportDebuggerEvaluationThreadResumed(m_state.LoggingContext, threadId);
        }

        private void NotifyDebuggerThreadStopped(int threadId, string reason, string message)
        {
            m_debugger.SendEvent(new StoppedEvent(threadId, reason, message));
        }

        private void WriteToDebuggerConsole(string message)
        {
            m_debugger.SendEvent(new OutputEvent("console", message));
        }

        private static INodeTracker GetStatementTracker(Context context)
        {
            var debugState = context.DebugState;
            return debugState.NodeTracker ?? (debugState.NodeTracker = new CompoundStatementTracker());
        }

        private IBreakpointStore GetBreakpointStore(Context context)
        {
            var debugState = context.DebugState;
            return debugState.Breakpoints ?? (debugState.Breakpoints = BreakpointStoreFactory.CreateProxy(m_state.MasterBreakpoints));
        }

        private static void PreEvalUpdateLocalVarNames(Node node, Context context)
        {
            int index = -1;
            SymbolAtom name = SymbolAtom.Invalid;
            switch (node.Kind)
            {
                case SyntaxKind.AssignmentExpression:
                    var n1 = (AssignmentExpression)node;
                    index = n1.Index;
                    name = n1.LeftExpression;
                    break;
                case SyntaxKind.LocalReferenceExpression:
                    var n2 = (LocalReferenceExpression)node;
                    index = n2.Index;
                    name = n2.Name;
                    break;
                case SyntaxKind.IncrementDecrementExpression:
                    var n3 = (IncrementDecrementExpression)node;
                    index = n3.Index;
                    name = n3.Operand;
                    break;
            }

            if (index != -1)
            {
                context.SetLocalVarName(index, name);
            }
        }

        private static void PostEvalUpdateLocalVarNames(Node node, Context context)
        {
            if (node.Kind == SyntaxKind.VarStatement)
            {
                var varStmt = (VarStatement)node;
                context.SetLocalVarName(varStmt.Index, varStmt.Name);
                return;
            }
        }

        private static bool DoesStackEntryHaveSource(DisplayStackTraceEntry entry)
        {
            Contract.Requires(entry != null);

            return !(entry.Line > 0 && File.Exists(entry.File));
        }

        private static bool DoesTopStackHaveSource(DisplayStackTraceEntry[] callStack)
        {
            Contract.Requires(callStack.Length > 0);

            return DoesStackEntryHaveSource(callStack[0]);
        }

        private static string GetPauseReason(DebugAction action)
        {
            return
                action.Kind == DebugAction.ActionKind.StepIn ? "StepIn" :
                action.Kind == DebugAction.ActionKind.StepOut ? "StepOut" :
                action.Kind == DebugAction.ActionKind.StepOver ? "Next" :
                string.Empty;
        }

        private static bool IsSteppingAction(DebugAction debugAction)
        {
            return debugAction != null && debugAction.Kind != DebugAction.ActionKind.Continue;
        }

        private static bool IsApplyExpressionNode(Node node)
        {
            return node.Kind == SyntaxKind.ApplyExpression ||
                   node.Kind == SyntaxKind.ApplyExpressionWithTypeArguments;
        }

        private static bool IsCallStackShallower(Node node, Context context, ModuleLiteral env, DebugAction debugAction)
        {
            var callStackSize = EvaluationState.GetStackTrace(context, env, node).Length;
            return callStackSize < debugAction.StackSize;
        }
    }
}
