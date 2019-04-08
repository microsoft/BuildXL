// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Threading;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using JetBrains.Annotations;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Values
{
    /// <summary>
    /// Thunk.
    /// </summary>
    public sealed class Thunk
    {
        private sealed class QualifiedValue
        {
            /// <summary>
            /// The (eventual) value that this qualified entry may evaluate to.
            /// </summary>
            /// <remarks>
            /// While evaluation is going, the value is an instance of <see cref="Evaluation"/>.
            /// </remarks>
            public object Value;
        }

        private const int TableSize = 4;

        /// <summary>
        /// The map of evaluated qualified values (already evaluated once or being evaluated right now).
        /// </summary>
        private ConcurrentDictionary<QualifierId, QualifiedValue> m_qualifiedDictionary;

        /// <summary>
        /// Light-weight "map" that holds qualified entries for the first <see cref="TableSize"/> qualifiers.
        /// </summary>
        private readonly QualifiedValue[] m_qualifiedValues = new QualifiedValue[TableSize];

        /// <summary>
        /// Gets thunk expression.
        /// </summary>
        public Expression Expression { get; }

        /// <summary>
        /// Captured template reference in the context of the thunk position. Can be null if we are in V1.
        /// </summary>
        [CanBeNull]
        public Expression CapturedTemplateReference { get; }

        /// <summary>
        /// Returns the value of this thunk if it has been evaluated, or <code>null</code> otherwise.
        /// </summary>
        /// <remarks>
        /// TODO: FixMe. This property makes little sense in the presence of qualifiers.
        /// This property is currently being used only by the debugger!
        /// </remarks>
        public object Value
        {
            get
            {
                var asQualifiedEntry = m_qualifiedValues[0];

                if (asQualifiedEntry != null)
                {
                    return ReturnUnlessEvaluation(asQualifiedEntry.Value);
                }

                return null;

                object ReturnUnlessEvaluation(object obj) => (obj is Evaluation) ? null : ((EvaluationResult)obj).Value;
            }
        }

        /// <nodoc />
        public Thunk(Expression expression, [CanBeNull]Expression capturedTemplateReference)
        {
            Contract.Requires(expression != null);

            Expression = expression;
            CapturedTemplateReference = capturedTemplateReference;
        }

        /// <summary>
        /// Evaluates this thunk in a new named context
        /// </summary>
        /// <remarks>
        /// V2-specific.
        /// </remarks>
        public EvaluationResult EvaluateWithNewNamedContext(Context context, ModuleLiteral module, FullSymbol contextName, LineInfo location)
        {
            Contract.Assert(CapturedTemplateReference != null);

            // We evaluate the captured template that was captured for this thunk in the same way (context and module) the thunk will be
            using (var frame = EvaluationStackFrame.Empty())
            {
                var templateValue = CapturedTemplateReference.Eval(context, module, frame);
                if (templateValue.IsErrorValue)
                {
                    return EvaluationResult.Error;
                }

                var factory = MutableContextFactory.Create(this, contextName, module, templateValue.Value, location);

                return EvaluateWithNewNamedContextAndTemplate(context, ref factory);
            }
        }

        /// <summary>
        /// Evaluates this thunk in a new named context
        /// </summary>
        /// <remarks>
        /// V1-specific. Still used to evaluate configuration-related files in V2 (and for the whole V1 legacy evaluation).
        /// </remarks>
        public EvaluationResult LegacyEvaluateWithNewNamedContext(ImmutableContextBase context, ModuleLiteral module, FullSymbol contextName, LineInfo location)
        {
            // There is no captured template value in V1
            var factory = MutableContextFactory.Create(this, contextName, module, templateValue: null, location: location);
            return EvaluateWithNewNamedContextAndTemplate(context, ref factory);
        }

        private EvaluationResult EvaluateWithNewNamedContextAndTemplate(ImmutableContextBase context, ref MutableContextFactory factory)
        {
            using (var frame = EvaluationStackFrame.Empty())
            {
                return Evaluate(
                    context: context,
                    env: factory.Module,
                    args: frame,
                    factory: ref factory);
            }
        }

        /// <summary>
        /// Evaluates thunk by lazily creating the context only if the thunk has not been evaluated yet.
        /// </summary>
        public EvaluationResult Evaluate(ImmutableContextBase context, ModuleLiteral env, EvaluationStackFrame args, ref MutableContextFactory factory)
        {
            context.EvaluationScheduler.CancellationToken.ThrowIfCancellationRequested();

            QualifierValue qualifier = env.GetFileQualifier();

            // Looking in the optimized map if the qualifier id is small enough.
            QualifiedValue qualifiedEntry;
            if (qualifier.QualifierId.Id < TableSize)
            {
                qualifiedEntry = GetQualifiedValue(qualifier.QualifierId.Id);
            }
            else
            {
                // Falling back to more memory intensive concurrent dictionary.
                EnsureDictionaryIsInitialized();
                qualifiedEntry = m_qualifiedDictionary.GetOrAdd(qualifier.QualifierId, new QualifiedValue());
            }

            var qualifiedValue = qualifiedEntry.Value;

            var result = (qualifiedValue == null || qualifiedValue is Evaluation)
                ? QualifiedEvaluate(ref qualifiedEntry.Value, context, env, args, ref factory)
                : (EvaluationResult)qualifiedValue;

            // Cancel evaluation if (1) result is an error value, (2) we are configured to cancel evaluation after first failure, and (3) cancellation hasn't already been requested
            if (result.IsErrorValue &&
                context.FrontEndHost.FrontEndConfiguration.CancelEvaluationOnFirstFailure() &&
                !context.EvaluationScheduler.CancellationToken.IsCancellationRequested)
            {
                context.Logger.EvaluationCancellationRequestedAfterFirstFailure(context.LoggingContext);
                context.EvaluationScheduler.Cancel();
            }

            return result;
        }

        private QualifiedValue GetQualifiedValue(int qualifierId)
        {
            var resultCandidate = Volatile.Read(ref m_qualifiedValues[qualifierId]);

            if (resultCandidate != null)
            {
                return resultCandidate;
            }

            var newCandidate = new QualifiedValue();

            // Changing the array if the value is still null.
            var oldValue = Interlocked.CompareExchange(ref m_qualifiedValues[qualifierId], newCandidate, null);

            // CompareExchange returns null if we won, so need to null-coalesce with a new value in this case.
            return oldValue ?? newCandidate;
        }

        private void EnsureDictionaryIsInitialized()
        {
            if (m_qualifiedDictionary == null)
            {
                lock (this)
                {
                    if (m_qualifiedDictionary == null)
                    {
                        m_qualifiedDictionary = new ConcurrentDictionary<QualifierId, QualifiedValue>();
                    }
                }
            }
        }

        private EvaluationResult QualifiedEvaluate(
            ref object value,
            ImmutableContextBase context,
            ModuleLiteral env,
            EvaluationStackFrame args,
            ref MutableContextFactory factory)
        {
            // Contract.Ensures(Contract.Result<object>() != null && !(Contract.Result<object>() is Evaluation));

            // do we have a real value yet?
            var currentValue = Volatile.Read(ref value);
            var currentEvaluation = currentValue as Evaluation;
            if (currentValue != currentEvaluation)
            {
                Contract.Assert(currentValue is EvaluationResult);

                // so it's not null, and it's not an evaluation => we have a real value!
                return (EvaluationResult)currentValue;
            }

            // no real value yet, let's try to create a new evaluation
            if (currentEvaluation == null)
            {
                var newEvaluation = new Evaluation(
                    context.EvaluatorConfiguration.CycleDetectorStartupDelay,
                    context.EvaluatorConfiguration.CycleDetectorIncreasePriorityDelay);
                currentValue = Interlocked.CompareExchange(ref value, newEvaluation, null);
                currentEvaluation = currentValue as Evaluation;
                if (currentValue != currentEvaluation)
                {
                    Contract.Assert(currentValue is EvaluationResult);

                    // so it's not null, and it's not an evaluation => we have a real value! (and the CompareExchange must have failed)
                    return (EvaluationResult)currentValue;
                }

                if (currentValue == null)
                {
                    using (var newLocalMutableContext = factory.Create(context))
                    {
                        try
                        {
                            var newValue = Expression.Eval(newLocalMutableContext, env, args);
                            newEvaluation.SetValue(ref value, newValue);
                            return newValue;
                        }
                        catch
                        {
                            // No actual value was ever set --- this means that Expression.Eval failed, and most likely threw an exception!
                            // Let's propagate this result, so that anyone else waiting for us gets unblocked.
                            newEvaluation.Cancel(EvaluationStatus.Crash);
                            throw;
                        }
                        finally
                        {
                            Contract.Assert(!newLocalMutableContext.HasChildren);

                            // just before the newly created context get disposed, we want to assert that all of its child contexts have already been disposed
                        }
                    }
                }

                // there's already an ongoing evaluation! (and the CompareExchange must have failed) we fall through...
            }

            return QualifiedEvaluateWithCycleDetection(ref value, context, env, currentEvaluation);
        }

        private EvaluationResult QualifiedEvaluateWithCycleDetection(ref object value, ImmutableContextBase context, ModuleLiteral env, Evaluation currentEvaluation)
        {
            // Someone else is already busy evaluating this thunk. Let's wait...
            EvaluationStatus result;
            var cycleDetector = context.FrontEndHost.CycleDetector;

            if (cycleDetector != null)
            {
                using (cycleDetector.AddValuePromiseChain(
                    valuePromiseChainGetter: () => GetValuePromiseChain(context, env),
                    cycleAnnouncer: () => currentEvaluation.Cancel(EvaluationStatus.Cycle)))
                {
                    currentEvaluation.Wait(cycleDetector);
                    result = currentEvaluation.Result;
                    if ((result & EvaluationStatus.Value) != 0)
                    {
                        var currentValue = Volatile.Read(ref value);
                        Contract.Assert(currentValue is EvaluationResult);
                        return (EvaluationResult)currentValue;
                    }
                }
            }
            else
            {
                currentEvaluation.Wait();
                result = currentEvaluation.Result;
                if ((result & EvaluationStatus.Value) != 0)
                {
                    var currentValue = Volatile.Read(ref value);
                    Contract.Assert(currentValue is EvaluationResult);
                    return (EvaluationResult)currentValue;
                }
            }

            // Evaluation got canceled --- we hit a cycle (or deadlock)!
            if ((result & EvaluationStatus.Cycle) != 0)
            {
                context.Errors.ReportCycle(env, Expression.Location);
                currentEvaluation.SetValue(ref value, EvaluationResult.Error);
                return EvaluationResult.Error;
            }

            // Evaluation crashed, which means that Expression.Eval had failed, and had thrown an exception.
            if ((result & EvaluationStatus.Crash) != 0)
            {
                // Crash has been reported at the crash site.
                currentEvaluation.SetValue(ref value, EvaluationResult.Error);
                return EvaluationResult.Error;
            }

            return EvaluationResult.Error;
        }

        /// <summary>
        /// Computes chain of active value promises. A value promise is identified by a thunk and the qualifier under which is being evaluated.
        /// </summary>
        /// <remarks>
        /// This method will be evaluated on the separate cycle detector thread.
        /// It is possible that there is no cycle,
        /// and that the actual evaluation thread continues before or while the chain is actually computed.
        /// In that case, the state observed by this method will be out of date.
        /// However, the evaluation thread will dispose the object returned by the <code>AddChain</code> call before the state will get out of date,
        /// and the dispose call indicates to the cycle detector that this chain is no longer to be considered.
        /// </remarks>
        private IValuePromise[] GetValuePromiseChain(ImmutableContextBase context, ModuleLiteral env)
        {
            var chain = new List<IValuePromise> { new ValuePromiseFromThunk(this, env.Qualifier.QualifierId) };

            // Let's take a walk along all contexts and collect the active thunks
            for (var c = context; c != null; c = c.ParentContext)
            {
                if (c.TopLevelValueInfo != null)
                {
                    chain.Add(new ValuePromiseFromThunk(c.TopLevelValueInfo.ActiveThunk, c.LastActiveModuleQualifier.QualifierId));
                }
            }

            return chain.ToArray();
        }

        [Flags]
        private enum EvaluationStatus
        {
            /// <summary>
            /// Not yet finished
            /// </summary>
            None,

            /// <summary>
            /// Canceled because of a detected cycle
            /// </summary>
            Cycle = 0x1,

            /// <summary>
            /// Canceled because of a crash (internal error)
            /// </summary>
            Crash = 0x2,

            /// <summary>
            /// We got a value!
            /// </summary>
            Value = 0x4,
        }

        /// <summary>
        /// A special value indicating that an evaluation is currently ongoing
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable", Justification = "Everything get's disposed internally by design.")]
        private sealed class Evaluation
        {
            /// <summary>
            /// Event; only non-null when requested by another thread
            /// </summary>
            private ManualResetEvent m_event;

            /// <summary>
            /// Count of how many threads are waiting for the event; event may be disposed when count goes back to 0.
            /// </summary>
            private int m_eventWaiters;

            private EvaluationStatus m_result;

            public EvaluationStatus Result => m_result;

            private readonly TimeSpan m_startupDelay;
            private readonly TimeSpan m_increasePriorityDelay;

            /// <nodoc />
            public Evaluation(TimeSpan startupDelay, TimeSpan increasePriorityDelay)
            {
                m_startupDelay = startupDelay;
                m_increasePriorityDelay = increasePriorityDelay;
            }

            /// <summary>
            /// Sets value and notifies any waiting threads
            /// </summary>
            public void SetValue(ref object value, EvaluationResult newValue)
            {
                // Contract.Requires(newValue != null);
                lock (this)
                {
                    // If we got canceled because of a deadlock or cycle, the only allowed value is the error value.
                    Contract.Assume(((m_result & EvaluationStatus.Cycle) == 0) || newValue.IsErrorValue);

                    // If the evaluation crashed, the only allowed value is the error value.
                    Contract.Assume(((m_result & EvaluationStatus.Crash) == 0) || newValue.IsErrorValue);

                    // If a value was already set, then we can only ever 'set' the same value again. This may happen in the case of cycles/deadlocks,
                    // where the error value is first set by a different thread that detected the cycle/deadlock, and then it's set again
                    // as the evaluation stack unwinds (minor implementation detail).
                    Contract.Assume(((m_result & EvaluationStatus.Value) == 0) || ((EvaluationResult)value).Value == newValue.Value);

                    value = newValue;
                    m_result |= EvaluationStatus.Value;
                    m_event?.Set();
                }
            }

            /// <summary>
            /// Indicates that the evaluation got canceled because of a cycle (or deadlock), and notifies any waiting threads
            /// </summary>
            public void Cancel(EvaluationStatus result)
            {
                Contract.Requires(result == EvaluationStatus.Cycle || result == EvaluationStatus.Crash);
                lock (this)
                {
                    m_result |= result;
                    m_event?.Set();
                }
            }

            /// <summary>
            /// Waits until evaluation gets cancelled or a value is set
            /// </summary>
            public void Wait(ICycleDetector cycleDetector = null)
            {
                lock (this)
                {
                    if (m_result != EvaluationStatus.None)
                    {
                        return;
                    }

                    m_eventWaiters++;
                    if (m_event == null)
                    {
                        m_event = new ManualResetEvent(false);
                    }
                }

                if (cycleDetector == null)
                {
                    m_event.WaitOne();
                }
                else
                {
                    // wait for evaluation in separate thread to finish
                    m_event.WaitOne(m_startupDelay);

                    // at first, wait for a reasonable amount of time without actually starting up cycle detector
                    bool hasResult;
                    lock (this)
                    {
                        hasResult = m_result != EvaluationStatus.None;
                    }

                    if (!hasResult)
                    {
                        // after the first delay, evaluation is still neither canceled nor finished; let's make sure the cycle detector is actually started up
                        cycleDetector.EnsureStarted();
                        m_event.WaitOne(m_increasePriorityDelay);

                        // second, wait for a reasonable amount of time without tinkering with priorities
                        lock (this)
                        {
                            hasResult = m_result != EvaluationStatus.None;
                        }

                        if (!hasResult)
                        {
                            // after the second delay, evaluation is still neither canceled nor finished; let's raise priority of cycle detector while we keep waiting
                            using (cycleDetector.IncreasePriority())
                            {
                                m_event.WaitOne();

                                // third, just keep waiting until signaled, because of cancellation of because evaluation finishes
                            }
                        }
                    }
                }

                lock (this)
                {
                    Contract.Assume(m_result != EvaluationStatus.None);
                    if (--m_eventWaiters == 0)
                    {
                        m_event.Dispose();
                        m_event = null;
                    }
                }
            }
        }

        /// <summary>
        /// Special mutable context factory used by thunks.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1815:ShouldOverrideEquals")]
        // TODO: use ref struct and pass it by in once migrated to C# 7.2
        public readonly struct MutableContextFactory
        {
            /// <nodoc />
            public Thunk ActiveThunk { get; }

            /// <nodoc />
            public FullSymbol ContextName { get; }

            /// <nodoc />
            public ModuleLiteral Module { get; }

            /// <nodoc />
            public object TemplateValue { get; }

            /// <nodoc />
            public LineInfo Location { get; }

            /// <nodoc />
            private MutableContextFactory(Thunk activeThunk, FullSymbol contextName, ModuleLiteral module, object templateValue, LineInfo location)
            {
                ActiveThunk = activeThunk;
                ContextName = contextName;
                Module = module;
                TemplateValue = templateValue;
                Location = location;
            }

            /// <nodoc />
            public static MutableContextFactory Create(
                Thunk activeThunk,
                FullSymbol contextName,
                ModuleLiteral module,
                object templateValue,
                LineInfo location)
            {
                return new MutableContextFactory(activeThunk, contextName, module, templateValue, location);
            }

            /// <nodoc />
            public Context Create(ImmutableContextBase context)
            {
                return context.CreateWithName(ActiveThunk, ContextName, Module, TemplateValue, Location);
            }
        }

        /// <summary>
        /// A thunk together with a qualifier always yield the same value
        /// </summary>
        private sealed class ValuePromiseFromThunk : IValuePromise, IEquatable<ValuePromiseFromThunk>
        {
            private Thunk Thunk { get; }

            private QualifierId QualifierId { get; }

            /// <nodoc/>
            public ValuePromiseFromThunk(Thunk thunk, QualifierId qualifierId)
            {
                Contract.Requires(thunk != null);
                Contract.Requires(qualifierId != QualifierId.Invalid);

                Thunk = thunk;
                QualifierId = qualifierId;
            }

            /// <inheritdoc/>
            public bool Equals(ValuePromiseFromThunk other)
            {
                return Thunk.Equals(other.Thunk)
                       && QualifierId.Equals(other.QualifierId);
            }

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj))
                {
                    return false;
                }

                return obj is ValuePromiseFromThunk valuePromise && Equals(valuePromise);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return HashCodeHelper.Combine(Thunk.GetHashCode(), QualifierId.GetHashCode());
            }
        }
    }
}
