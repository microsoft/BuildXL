// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities.Instrumentation.Common;
using VSCode.DebugProtocol;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.FrontEnd.Script.Debugger
{
    /// <summary>
    ///     An implementation of <see cref="IDecorator{T}"/> which takes a debugger and a state.
    /// </summary>
    public sealed class EvaluationDecorator : IDecorator<EvaluationResult>
    {
        private readonly IDebugger m_debugger;
        private readonly NodeEvalManager m_nodeEvalManager;
        private readonly bool m_breakOnExit;

        /// <summary>Constructor, accepting a task returning an <see cref="IDebugger"/></summary>
        public EvaluationDecorator(IDebugger debugger, DebuggerState state, bool breakOnExit)
        {
            Contract.Requires(debugger != null);
            Contract.Requires(state != null);

            m_debugger = debugger;
            m_breakOnExit = breakOnExit;
            m_nodeEvalManager = new NodeEvalManager(state, debugger);
        }

        /// <summary>
        ///     Waits until the result of the task becomes available; if successful, waits until this session has
        ///     been initialized (see <code cref="ISession.WaitSessionInitialized"/>), after which delegates the
        ///     call to <code cref="NodeEvalManager.NodeEval"/> which is responsible for driving node evaluation
        ///     and implementing debugging features around it.
        /// </summary>
        public EvaluationResult EvalWrapper(Node node, Context context, ModuleLiteral env, EvaluationStackFrame args, Func<EvaluationResult> continuation)
        {
            Contract.Requires(node != null);
            Contract.Requires(context != null);
            Contract.Requires(args != null);
            Contract.Requires(continuation != null);

            m_debugger.Session.WaitSessionInitialized();
            return m_nodeEvalManager.NodeEval(node, context, env, args, continuation);
        }

        /// <summary>
        ///     If <paramref name="diagnostic"/> is an error, adds it to <paramref name="context"/>'s DebugState.
        /// </summary>
        public void NotifyDiagnostics(Context context, Diagnostic diagnostic)
        {
            Contract.Requires(context != null);
            Contract.Requires(diagnostic != null);

            if (diagnostic.Level == System.Diagnostics.Tracing.EventLevel.Error ||
                diagnostic.Level == System.Diagnostics.Tracing.EventLevel.Critical)
            {
                context.DebugState.AddError(diagnostic);
            }
        }

        /// <summary>
        ///     If 'breakOnExit' was requested, delegates the call to <see cref="NodeEvalManager.PauseHost"/>
        ///     (which should pause the current thread).
        /// </summary>
        public void NotifyEvaluationFinished(bool success, IEnumerable<IModuleAndContext> contexts)
        {
            Contract.Requires(contexts != null);

            if (m_breakOnExit)
            {
                m_nodeEvalManager.PauseHost(contexts);
            }
        }
    }

    /// <summary>
    ///     A simple decorator that doesn't do anything.
    /// </summary>
    public sealed class EmptyDecorator : IDecorator<EvaluationResult>
    {
        /// <inheritdoc />
        public EvaluationResult EvalWrapper(Node node, Context context, ModuleLiteral env, EvaluationStackFrame args, Func<EvaluationResult> continuation) => continuation();

        /// <inheritdoc />
        public void NotifyDiagnostics(Context context, Diagnostic diagnostic) { }

        /// <inheritdoc />
        public void NotifyEvaluationFinished(bool success, IEnumerable<IModuleAndContext> contexts) { }
    }

    /// <summary>
    ///     An implementation of <see cref="IDecorator{T}"/> which takes a task returning an <see cref="IDebugger"/>,
    ///     so that both <see cref="EvalWrapper"/> and <see cref="NotifyDiagnostics"/> block until the result of the task
    ///     becomes available.
    /// </summary>
    public sealed class LazyDecorator : IDecorator<EvaluationResult>
    {
        private readonly Task<IDebugger> m_debugger;
        private readonly bool m_breakOnFinish;

        private IDecorator<EvaluationResult> m_decorator;

        private IDecorator<EvaluationResult> Decorator => m_decorator ?? (m_decorator = CreateDecorator());

        private IDecorator<EvaluationResult> CreateDecorator()
        {
            Contract.Ensures(Contract.Result<IDecorator<EvaluationResult>>() != null);

            var debugger = m_debugger.Result;
            return debugger != null
                ? (IDecorator<EvaluationResult>)new EvaluationDecorator(debugger, ((DebugSession)debugger?.Session).State, m_breakOnFinish)
                : (IDecorator<EvaluationResult>)new EmptyDecorator();
        }

        /// <summary>Constructor, accepting a task returning an <see cref="IDebugger"/> and whether to break on finish.</summary>
        public LazyDecorator(Task<IDebugger> debugger, bool breakOnFinish)
        {
            Contract.Requires(debugger != null);

            m_debugger = debugger;
            m_breakOnFinish = breakOnFinish;
        }

        /// <summary>
        ///     Waits until the result of the task becomes available; upon which uses an <see cref="EvaluationDecorator"/>
        ///     to delegate this method to it.
        /// </summary>
        public EvaluationResult EvalWrapper(Node node, Context context, ModuleLiteral env, EvaluationStackFrame args, Func<EvaluationResult> continuation)
            => Decorator.EvalWrapper(node, context, env, args, continuation);

        /// <summary>
        ///     Waits until the result of the task becomes available; upon which uses an <see cref="EvaluationDecorator"/>
        ///     to delegate this method to it.
        /// </summary>
        public void NotifyDiagnostics(Context context, Diagnostic diagnostic) => Decorator.NotifyDiagnostics(context, diagnostic);

        /// <summary>
        ///     Waits until the result of the task becomes available; upon which uses an <see cref="EvaluationDecorator"/>
        ///     to delegate this method to it.
        /// </summary>
        public void NotifyEvaluationFinished(bool success, IEnumerable<IModuleAndContext> contexts) => Decorator.NotifyEvaluationFinished(success, contexts);
    }
}
