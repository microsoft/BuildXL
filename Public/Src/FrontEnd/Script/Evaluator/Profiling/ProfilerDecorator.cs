// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using TypeScript.Net.Utilities;

namespace BuildXL.FrontEnd.Script.Evaluator.Profiling
{
    /// <summary>
    /// Profiles function calls by registering elapsed time and provenance information
    /// </summary>
    /// TODO: Add exclusive time. This needs a slightly larger infrastructure change or a more complicated tracking mechanism here
    public sealed class ProfilerDecorator : IDecorator<EvaluationResult>
    {
        private readonly Stopwatch m_stopwatch;
        private bool m_evaluationFinished;

        // We need to know what each callsite references to. We store here functor callsite nodes
        // so when a further evaluation reaches that node, its value gets updated.
        private readonly ConcurrentDictionary<Node, object> m_functorsPendingEvaluation;

        // Each apply expression call will be profiled and stored here
        private readonly ConcurrentQueue<ProfiledFunctionCall> m_profilerEntries;

        /// <nodoc/>
        public ProfilerDecorator()
        {
            m_profilerEntries = new ConcurrentQueue<ProfiledFunctionCall>();
            m_functorsPendingEvaluation = new ConcurrentDictionary<Node, object>();
            m_stopwatch = new Stopwatch();
            m_stopwatch.Start();
            m_evaluationFinished = false;
        }

        /// <nodoc/>
        public EvaluationResult EvalWrapper(Node node, Context context, ModuleLiteral env, EvaluationStackFrame args, Func<EvaluationResult> continuation)
        {
            Contract.Requires(node != null);
            Contract.Requires(context != null);
            Contract.Requires(args != null);
            Contract.Requires(continuation != null);

            // We want to profile apply expression nodes only
            var applyExpression = node as ApplyExpression;
            if (applyExpression != null)
            {
                // We record that this functor needs its evaluated value by adding it to the dictionary with a null value if it is not there yet
                m_functorsPendingEvaluation.TryAdd(applyExpression.Functor, null);

                // Register start time and then call the continuation
                var startTime = m_stopwatch.ElapsedMilliseconds;

                var applyResult = continuation();

                var endTime = m_stopwatch.ElapsedMilliseconds;

                // After running the continuation, the functor value should not be pending anymore. So we retrieve its id and location
                Contract.Assert(m_functorsPendingEvaluation.ContainsKey(applyExpression.Functor) && m_functorsPendingEvaluation[applyExpression.Functor] != null);
                GetFunctorIdNameAndLocation(context, m_functorsPendingEvaluation[applyExpression.Functor], out int functorId, out string functorName, out string functorLocation);

                // We cannot really remove the entry here since there might be other parallel evaluations that depend on it
                // TODO: we could try something out if memory turns out to be a problem.
                var entry = new ProfiledFunctionCall(
                    callsiteInvocation: node.ToDisplayString(context),
                    callsiteLocation: node.Location.ToLocationData(env.Path),
                    durationInclusive: endTime - startTime,
                    qualifier: env.Qualifier.QualifierId.ToDisplayString(context),
                    functionId: functorId,
                    functionName: functorName,
                    functionLocation: functorLocation);

                m_profilerEntries.Enqueue(entry);

                return applyResult;
            }

            // In this case we don't profile the evaluation call, but we check if this happens to be the functor for which we need its value for a parent evaluation
            var result = continuation();

            if (m_functorsPendingEvaluation.TryGetValue(node, out object existingResult) && existingResult == null)
            {
                m_functorsPendingEvaluation[node] = result.Value;
            }

            return result;
        }

        private static void GetFunctorIdNameAndLocation(Context context, object functorValue, out int functorId, out string functorName, out string functorLocation)
        {
            var closure = functorValue as Closure;
            if (closure != null)
            {
                functorLocation = closure.Env.CurrentFileModule == null ? "<ambient call>" : closure.Function.Location.ToLocationData(closure.Env.Path).ToString(context.PathTable);
                functorId = closure.Function.GetHashCode();
                functorName = closure.Function.Name.IsValid ? closure.Function.Name.ToString(context.StringTable) : closure.Function.ToDisplayString(context);

                return;
            }

            var callableValue = functorValue as CallableValue;
            if (callableValue != null)
            {
                functorLocation = "<ambient call>";
                functorId = callableValue.CallableMember.GetHashCode();
                functorName = callableValue.CallableMember.Name.ToString(context.StringTable);
                return;
            }

            if (functorValue.IsErrorValue() || functorValue == UndefinedValue.Instance)
            {
                functorLocation = "<no location available>";
                functorId = 0;
                functorName = functorValue.IsErrorValue() ? "error" : "undefined";
                return;
            }

            Contract.Assume(false, "Expecting a closure, a callable value, an error or undefined value instance, but got " + functorValue.GetType());
            functorLocation = null;
            functorId = 0;
            functorName = null;
        }

        /// <nodoc/>
        public void NotifyDiagnostics(Context context, Diagnostic diagnostic)
        {
            Contract.Requires(context != null);
            Contract.Requires(diagnostic != null);

            // The profiler doesn't care about diagnostics (so far)
        }

        /// <nodoc/>
        public void NotifyEvaluationFinished(bool success, IEnumerable<IModuleAndContext> contexts)
        {
            Contract.Requires(contexts != null);

            m_evaluationFinished = true;
        }

        /// <summary>
        /// Returns a collection of profiled entries that resulted from evaluation
        /// </summary>
        /// <remarks>
        /// Evaluation should be finished in order to call this function
        /// </remarks>
        public IReadOnlyCollection<ProfiledFunctionCall> GetProfiledEntries()
        {
            Contract.Assert(m_evaluationFinished, "Evaluation should be finished to be able to retrieve the profiled entries");
            return m_profilerEntries.ToArray();
        }
    }
}
