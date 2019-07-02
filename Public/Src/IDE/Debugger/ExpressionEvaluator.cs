// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.Core;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.RuntimeModel;
using BuildXL.FrontEnd.Script.RuntimeModel.AstBridge;
using BuildXL.FrontEnd.Script.Tracing;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.FrontEnd.Script.Debugger
{
    /// <summary>
    /// Responsible for evaluating expressions in 'immediate' mode
    /// </summary>
    public sealed class ExpressionEvaluator
    {
        private readonly DebuggerState m_state;

        // The logger is reused across invocations, so it needs to be cleared out before parsing/evaluation occurs
        private readonly Logger m_logger = Logger.CreateLogger(preserveLogEvents: true, forwardDiagnosticsTo: null, notifyContextWhenErrorsAreLogged: false);

        private static readonly ConfigurationImpl s_configuration = new ConfigurationImpl();

        private static RuntimeModelFactory s_parser;

        /// <nodoc />
        public ExpressionEvaluator(DebuggerState state)
        {
            m_state = state;

            var configuration = new AstConversionConfiguration(
                policyRules: Enumerable.Empty<string>(),
                disableLanguagePolicies: false);

            s_parser = new RuntimeModelFactory(
                m_logger,
                m_state.LoggingContext,
                new FrontEndStatistics(),
                configuration,
                workspace: null);
        }

        /// <summary>
        /// Evaluates an expression in the current debugger state context
        /// </summary>
        internal Possible<ObjectContext, EvaluateFailure> EvaluateExpression(FrameContext frameContext, string expressionString)
        {
            var evalState = (EvaluationState)m_state.GetThreadState(frameContext.ThreadId);
            var context = evalState.Context;
            var moduleLiteral = evalState.GetEnvForFrame(frameContext.FrameIndex);

            var frontEnd = new DScriptFrontEnd(new FrontEndStatistics());
            frontEnd.InitializeFrontEnd(context.FrontEndHost, context.FrontEndContext, s_configuration);

            // We clear the logger before using it.
            var isClear = m_logger.TryClearCapturedDiagnostics();

            // This logger should only be used in the context of one thread, so it should always be possible to clear it
            Contract.Assert(isClear);

            RuntimeModelContext runtimeModelContext = new RuntimeModelContext(
                    context.FrontEndHost,
                    context.FrontEndContext,
                    m_logger,
                    context.Package);

            // We recreate the local scope so the expression is parsed using the same local variables indexes
            // than the context where it is going to be evaluated
            var localScope = BuildLocalScopeForLocalVars(context, evalState.GetStackEntryForFrame(frameContext.FrameIndex));
            var expression = s_parser.ParseExpression(runtimeModelContext, context.Package.Path, expressionString, localScope, useSemanticNameResolution: false);

            // If parsing failed, we report it and return
            // VS code only displays the first error that is sent. So we only send the first one.
            // An alternative would be to concatenate all errors and send them as a single message, but new lines are not respected by VS Code,
            // so the concatenation is not very legible.
            // Anyway, the first error should be good enough for almost all cases
            if (expression == null)
            {
                Contract.Assert(runtimeModelContext.Logger.CapturedDiagnostics.Count > 0);
                var diagnostic = runtimeModelContext.Logger.CapturedDiagnostics[0];
                return new EvaluateFailure(diagnostic);
            }

            // We clear the logger again since it may contain warnings that didn't prevent the parser from finishing successfully
            isClear = m_logger.TryClearCapturedDiagnostics();
            Contract.Assert(isClear);

            object expressionResult;

            // We temporary override the context logger so it doesn't affect the normal evaluation
            using (var expressionContext = new SnippetEvaluationContext(context, m_logger))
            {
                expressionResult = expression.Eval(expressionContext.GetContextForSnippetEvaluation(), moduleLiteral, evalState.GetArgsForFrame(frameContext.FrameIndex)).Value;

                // If evaluation failed, we report it and return
                if (expressionResult.IsErrorValue())
                {
                    Contract.Assert(context.Logger.CapturedDiagnostics.Count > 0);
                    var diagnostic = context.Logger.CapturedDiagnostics[0];
                    return new EvaluateFailure(diagnostic);
                }
            }

            return new ObjectContext(context, expressionResult);
        }

        /// <summary>
        /// Creates a simulation of a local scope such that passed local variable indexes match
        /// </summary>
        private static FunctionScope BuildLocalScopeForLocalVars(Context context, StackEntry stackEntry)
        {
            var stringTable = context.StringTable;
            var localScope = new FunctionScope();

            var localVariables = DebugInfo.ComputeCurrentLocals(stackEntry);

            if (localVariables.Count == 0)
            {
                return localScope;
            }

            // Sort variables by index
            var localsByIndex = new SortedDictionary<int, ILocalVar>();
            foreach (var localVar in localVariables)
            {
                localsByIndex[localVar.Index] = localVar;
            }

            // Construct a local scope where variable indexes are respected, filling with dummy variables if there are holes in the index range
            int currentIndex = 0;
            foreach (int index in localsByIndex.Keys)
            {
                Contract.Assert(currentIndex <= index);

                while (index != currentIndex)
                {
                    var dummyIndex = localScope.AddVariable(SymbolAtom.Create(stringTable, "__dummy_var__" + currentIndex), default(UniversalLocation), isConstant: false);
                    Contract.Assert(dummyIndex != null);
                    currentIndex++;
                }

                var indexResult = localScope.AddVariable(localsByIndex[index].Name, default(UniversalLocation), isConstant: false);
                Contract.Assert(indexResult == index);
                currentIndex++;
            }

            return localScope;
        }
    }

    /// <summary>
    /// Auxiliary implementation of <see cref="Failure"/>, used by <see cref="ExpressionEvaluator.EvaluateExpression"/>
    /// </summary>
    internal sealed class EvaluateFailure : Failure
    {
        public Diagnostic Diagnostic { get; }

        public EvaluateFailure(Diagnostic diagnostic)
        {
            Diagnostic = diagnostic;
        }

        /// <inheritdoc />
        public override BuildXLException CreateException() => new BuildXLException(Describe());

        /// <inheritdoc />
        public override string Describe() => Diagnostic.FullMessage;

        /// <inheritdoc />
        public override BuildXLException Throw()
        {
            throw CreateException();
        }
    }
}
