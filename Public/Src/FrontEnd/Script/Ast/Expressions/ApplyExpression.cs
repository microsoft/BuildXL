// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using LineInfo = TypeScript.Net.Utilities.LineInfo;
using Type = BuildXL.FrontEnd.Script.Types.Type;
using SyntaxKind = TypeScript.Net.Types.SyntaxKind;

namespace BuildXL.FrontEnd.Script.Expressions
{
    /// <summary>
    /// Base class for application expression.
    /// </summary>
    public abstract class ApplyExpression : Expression
    {
        // Cache of resolved "function pointers" with the qualifier-specific module literals.
        // In many cases, the apply expression just calls the function in a form of 'callTheFunction(frame)'.
        // In this case, we can resolve a functor and save the result of it in the instance state.
        // The only caveat is qualifiers. The old approach was based on full expression evaluation that created a Closure instance
        // on each method invocation. That approach was not efficient in terms of performance and memory.
        private const int MaxQualifierId = 10;
        private FunctionLikeExpression m_functionToInvoke;
        private readonly FileModuleLiteral[] m_fileLiterals = new FileModuleLiteral[MaxQualifierId];

        private readonly Expression[] m_arguments;

        /// <nodoc />
        public Expression Functor { get; }

        /// <nodoc />
        public IReadOnlyList<Expression> Arguments => m_arguments;

        /// <nodoc />
        protected ApplyExpression(Expression functor, Expression[] arguments, LineInfo location)
            : base(location)
        {
            Contract.Requires(functor != null);
            Contract.Requires(arguments != null);
            Contract.RequiresForAll(arguments, a => a != null);

            Functor = functor;
            m_arguments = arguments;
        }

        /// <summary>
        /// Factory method that creates <see cref="ApplyExpression"/>
        /// depending on argument values.
        /// </summary>
        public static ApplyExpression Create(Expression functor, Expression[] arguments, LineInfo location)
        {
            return Create(functor, CollectionUtilities.EmptyArray<Type>(), arguments, location);
        }

        /// <summary>
        /// Factory method that creates <see cref="ApplyExpression"/> or <see cref="ApplyExpressionWithTypeArguments"/>
        /// depending on argument values.
        /// </summary>
        /// <remarks>
        /// This function creates memory efficient representation.
        /// This approach saves a lot of memory at runtime because in many cases function applications are used with very small number of arguments.
        /// </remarks>
        public static ApplyExpression Create(Expression functor, Type[] typeArguments, Expression[] arguments, LineInfo location)
        {
            if (typeArguments.Length != 0)
            {
                return new ApplyExpressionWithTypeArguments(functor, typeArguments, arguments, location);
            }

            return new NonGenericApplyExpression(functor, arguments, location);
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            var statisticHandler = InvocationStatisticHandler.Create(context);
            try
            {
                // If the functor is a named function, then we can skip closure creation and call the function directly.
                if (TryGetFunctionToInvoke(context, env, out var lambda, out var file))
                {
                    return InvokeFunction(context, env, frame, lambda, frame, file, ref statisticHandler);
                }

                var evaluatedValue = Functor.Eval(context, env, frame);

                if (evaluatedValue.IsErrorValue)
                {
                    // Error should have been reported during the evaluation of the functor.
                    return evaluatedValue;
                }

                // Note, need to check Functor type but not type of the evaluated value.
                if (evaluatedValue.IsUndefined && Functor is SelectorExpression selector)
                {
                    // Special case for unresolved selector to generate more specific error message.
                    return HandleUnresolvedSelector(context, env, frame, selector);
                }

                if (evaluatedValue.Value is CallableValue callableValue)
                {
                    return HandleCallableValue(context, env, frame, callableValue, ref statisticHandler);
                }

                if (evaluatedValue.Value is Closure closure)
                {
                    return InvokeFunction(context, env, frame, closure.Function, closure.Frame, closure.Env, ref statisticHandler);
                }

                context.Errors.ReportUnexpectedValueType(
                    env,
                    Functor,
                    evaluatedValue,
                    typeof(CallableValue),
                    typeof(Closure));
            }
            catch (ConvertException convertException)
            {
                // ConversionException derives from EvaluationException but should be handled separatedly,
                context.Errors.ReportUnexpectedValueTypeOnConversion(env, convertException, Location);
            }
            catch (EvaluationException e)
            {
                e.ReportError(context.Errors, env, Location, expression: this, context: context);
            }
            catch (OperationCanceledException)
            {
                return EvaluationResult.Canceled;
            }
            catch (Exception exception)
            {
                context.Errors.ReportUnexpectedAmbientException(env, exception, Location);

                // Getting here indicates a bug somewhere in the evaluator. Who knows what went wrong.
                // Let's re-throw and let some other global exception handler deal with it!
                throw;
            }
            finally
            {
                statisticHandler.TrackInvocation(context);
            }

            return EvaluationResult.Error;
        }

        /// <summary>
        /// Tries to get a resolved (cached) function and module literal for a current Functor.
        /// </summary>
        private bool TryGetFunctionToInvoke(Context context, ModuleLiteral env, out FunctionLikeExpression function, out FileModuleLiteral file)
        {
            // When decorators are supported, no optimizations should be used.
            var qualifierId = env.Qualifier.QualifierId.Id;
            if (context.HasDecorator || qualifierId >= MaxQualifierId)
            {
                function = null;
                file = null;
                return false;
            }

            // If the Functor is a regular named function call (i.e. FooBar in <code>function fooBar() {} const x = fooBar();</code>
            // Then we can resolve the function once and cache it, but we have to obtain the 'file literal'
            // that represents the target file for each qualifier.
            // That's why there is a single value for function and an array for module literals.

            file = m_fileLiterals[qualifierId];
            if (file != null)
            {
                // This is only possible when the function was already resolved at least once.
                function = m_functionToInvoke;
                return function != null;
            }

            // The cache is cold. Resolving the function if possible.
            if (Functor is LocationBasedSymbolReference locationBased)
            {
                locationBased.TryResolveFunction(context, env, out function, out file);

                if (function != null && file != null)
                {
                    m_fileLiterals[qualifierId] = file;
                    m_functionToInvoke = function;
                    return true;
                }
            }

            function = null;
            return false;
        }

        private static EvaluationResult HandleUnresolvedSelector(Context context, ModuleLiteral env, EvaluationStackFrame args, SelectorExpression selector)
        {
            // Functor is a selector expression, like a.foo() and selector just returned undefined.
            // This means that name resolution failed.
            var thisExpression = selector.ThisExpression.Eval(context, env, args);

            context.Errors.ReportMissingProperty(env, selector.Selector, thisExpression.Value, selector.Location);

            return EvaluationResult.Error;
        }

        private EvaluationResult HandleCallableValue(Context context, ModuleLiteral currentEnv, EvaluationStackFrame frame, CallableValue invocable, ref InvocationStatisticHandler statisticsHandler)
        {
            // functor is a member function or property
            if (invocable.CallableMember.MinArity > m_arguments.Length)
            {
                context.Errors.ReportApplyAmbientNumberOfArgumentsLessThanMinArity(
                    currentEnv,
                    Functor,
                    invocable.CallableMember.MinArity,
                    m_arguments.Length,
                    Location);

                return EvaluationResult.Error;
            }

            using (context.PrepareStackEntryForAmbient(this, currentEnv, frame))
            {
                if (context.IsStackOverflow)
                {
                    context.Errors.ReportStackOverflow(currentEnv, Location);
                    return EvaluationResult.Error;
                }

                switch (invocable.CallableMember.MaxArity)
                {
                    case 0:
                        return EvaluateAmbientValue0(context, frame, invocable, ref statisticsHandler);
                    case 1:
                        return EvaluateAmbientValue1(context, currentEnv, frame, invocable, ref statisticsHandler);
                    case 2:
                        return EvaluateAmbientValue2(context, currentEnv, frame, invocable, ref statisticsHandler);
                    default:
                        return EvaluateAmbientValueN(context, currentEnv, frame, invocable, ref statisticsHandler);
                }
            }
        }

        private static EvaluationResult EvaluateAmbientValue0(Context context, EvaluationStackFrame frame, CallableValue invocable, ref InvocationStatisticHandler statisticsHandler)
        {
            statisticsHandler.CaptureStatistics(invocable.CallableMember.Statistic);
            return invocable.Apply(context, frame);
        }

        private EvaluationResult EvaluateAmbientValue1(Context context, ModuleLiteral env, EvaluationStackFrame frame, CallableValue invocable, ref InvocationStatisticHandler statisticsHandler)
        {
            if (m_arguments.Length == 0)
            {
                return invocable.Apply(context, EvaluationResult.Undefined, frame);
            }

            var argValue = !invocable.CallableMember.Rest ? m_arguments[0].Eval(context, env, frame) : EvaluateRestArg(context, env, frame, 0);
            if (argValue.IsErrorValue)
            {
                return argValue;
            }

            statisticsHandler.CaptureStatistics(invocable.CallableMember.Statistic);
            return invocable.Apply(context, argValue, frame);
        }

        private EvaluationResult EvaluateAmbientValue2(Context context, ModuleLiteral env, EvaluationStackFrame frame, CallableValue invocable, ref InvocationStatisticHandler statisticsHandler)
        {
            if (m_arguments.Length == 0)
            {
                return invocable.Apply(context, EvaluationResult.Undefined, EvaluationResult.Undefined, frame);
            }

            if (m_arguments.Length == 1)
            {
                var argValue = m_arguments[0].Eval(context, env, frame);
                return argValue.IsErrorValue ? argValue : invocable.Apply(context, argValue, EvaluationResult.Undefined, frame);
            }

            var argValue0 = m_arguments[0].Eval(context, env, frame);

            if (argValue0.IsErrorValue)
            {
                return argValue0;
            }

            var argValue1 = !invocable.CallableMember.Rest ? m_arguments[1].Eval(context, env, frame) : EvaluateRestArg(context, env, frame, 1);

            if (argValue1.IsErrorValue)
            {
                return EvaluationResult.Error;
            }

            statisticsHandler.CaptureStatistics(invocable.CallableMember.Statistic);
            // TODO: change Apply to take EvaluationResult but not objects!
            return invocable.Apply(context, argValue0, argValue1, frame);
        }

        private EvaluationResult EvaluateAmbientValueN(Context context, ModuleLiteral env, EvaluationStackFrame frame, CallableValue invocable, ref InvocationStatisticHandler statisticsHandler)
        {
            int numOfParams = invocable.CallableMember.MaxArity < short.MaxValue ? invocable.CallableMember.MaxArity : m_arguments.Length;

            // TODO: switch values to use EvaluationResult to avoid boxing.
            var values = new EvaluationResult[numOfParams];

            int i = 0;

            for (; i < invocable.CallableMember.MinArity; ++i)
            {
                values[i] = m_arguments[i].Eval(context, env, frame);

                if (values[i].IsErrorValue)
                {
                    return EvaluationResult.Error;
                }
            }

            for (; i < numOfParams; ++i)
            {
                if (i >= m_arguments.Length)
                {
                    values[i] = EvaluationResult.Undefined;
                }
                else if (i < m_arguments.Length && i == numOfParams - 1 && invocable.CallableMember.Rest)
                {
                    values[i] = EvaluateRestArg(context, env, frame, i);
                }
                else
                {
                    values[i] = m_arguments[i].Eval(context, env, frame);
                }

                if (values[i].IsErrorValue)
                {
                    return EvaluationResult.Error;
                }
            }

            statisticsHandler.CaptureStatistics(invocable.CallableMember.Statistic);

            return invocable.Apply(context, values, frame);
        }

        private EvaluationResult EvaluateRestArg(Context context, ModuleLiteral env, EvaluationStackFrame frame, int startArgPos)
        {
            Contract.Requires(startArgPos < m_arguments.Length);

            var restArgumentsLength = m_arguments.Length - startArgPos;

            if (restArgumentsLength == 1)
            {
                // Special case if rest argument is of size 1, and it is of the form ...array.
                // This avoid multiple array constructions.
                if (m_arguments[startArgPos] is UnaryExpression ue && ue.OperatorKind == UnaryOperator.Spread)
                {
                    var argValue = m_arguments[startArgPos].Eval(context, env, frame);

                    if (argValue.IsErrorValue)
                    {
                        return argValue;
                    }

                    // If the argument is of the form spread operator, then we need to flatten it.
                    if (argValue.Value is ArrayLiteral)
                    {
                        return argValue;
                    }

                    context.Errors.ReportSpreadIsNotAppliedToArrayValue(env, ue.Expression, ue.Expression.Location);
                    return EvaluationResult.Error;
                }
            }

            var values = new List<EvaluationResult>(restArgumentsLength);

            int j = startArgPos;

            for (int i = 0; i < restArgumentsLength; ++i)
            {
                var argValue = m_arguments[j].Eval(context, env, frame);

                if (argValue.IsErrorValue)
                {
                    return argValue;
                }

                if (m_arguments[j] is UnaryExpression ue && ue.OperatorKind == UnaryOperator.Spread)
                {
                    // If the argument is of the form spread operator, then we need to flatten it.
                    if (argValue.Value is ArrayLiteral argArrayValue)
                    {
                        values.AddRange(argArrayValue.Values);
                    }
                    else
                    {
                        context.Errors.ReportSpreadIsNotAppliedToArrayValue(env, ue.Expression, ue.Expression.Location);
                        return EvaluationResult.Error;
                    }
                }
                else
                {
                    values.Add(argValue);
                }

                ++j;
            }

            return EvaluationResult.Create(
                ArrayLiteral.CreateWithoutCopy(values.ToArray(), m_arguments[startArgPos].Location, env.Path));
        }

        private static EvaluationResult EvaluateLambda(
            Context context,
            FunctionLikeExpression function,
            ModuleLiteral env,
            EvaluationStackFrame captures)
        {
            return function.Invoke(context, env, captures);
        }

        private EvaluationResult InvokeFunction(Context context, ModuleLiteral currentEnv, EvaluationStackFrame currentFrame, FunctionLikeExpression function, EvaluationStackFrame closureFrame, ModuleLiteral targetEnv, ref InvocationStatisticHandler statistics)
        {
            using (var frame = EvaluationStackFrame.Create(function, closureFrame.Frame))
            {
                if (!TryEvaluateArguments(context, currentEnv, currentFrame, function, frame))
                {
                    return EvaluationResult.Error;
                }

                statistics.CaptureStatistics(function.Statistic);

                using (function.IsAmbient
                    ? context.PrepareStackEntryForAmbient(this, currentEnv, frame)
                    : context.PushStackEntry(function, targetEnv, currentEnv, Location, frame))
                {
                    if (context.IsStackOverflow)
                    {
                        context.Errors.ReportStackOverflow(currentEnv, Location);
                        return EvaluationResult.Error;
                    }

                    return EvaluateLambda(context, function, targetEnv, frame);
                }
            }
        }

        private bool TryEvaluateArguments(
            Context context,
            ModuleLiteral currentEnv,
            EvaluationStackFrame currentFrame,
            FunctionLikeExpression targetFunction,
            EvaluationStackFrame targetFrame)
        {
            var numParams = targetFunction.Params;
            int argPos = 0;
            for (int j = 0; j < numParams; ++j)
            {
                EvaluationResult argValue;
                if (j >= m_arguments.Length)
                {
                    if (targetFunction.CallSignature.Parameters[j].ParameterKind == ParameterKind.Rest
                        && j == numParams - 1)
                    {
                        argValue = EvaluationResult.Create(ArrayLiteral.CreateWithoutCopy(CollectionUtilities.EmptyArray<EvaluationResult>(), default(LineInfo), currentEnv.Path));
                    }
                    else
                    {
                        argValue = EvaluationResult.Undefined;
                    }
                }
                else if (targetFunction.CallSignature.Parameters[j].ParameterKind == ParameterKind.Rest
                         && j == numParams - 1
                         && j < m_arguments.Length)
                {
                    var start = j;
                    var restLength = m_arguments.Length - start;
                    var rest = new List<EvaluationResult>(restLength);

                    for (int k = 0; k < restLength; ++k, ++j)
                    {
                        var restValue = m_arguments[j].Eval(context, currentEnv, currentFrame);

                        if (restValue.IsErrorValue)
                        {
                            return false;
                        }

                        if (m_arguments[j] is UnaryExpression ue && ue.OperatorKind == UnaryOperator.Spread)
                        {
                            // If the argument is of the form spread operator, then we need to flatten it.
                            if (restValue.Value is ArrayLiteral restArrayValue)
                            {
                                rest.AddRange(restArrayValue.Values);
                            }
                            else
                            {
                                context.Errors.ReportSpreadIsNotAppliedToArrayValue(currentEnv, ue.Expression, ue.Expression.Location);
                                return false;
                            }
                        }
                        else
                        {
                            rest.Add(restValue);
                        }
                    }

                    argValue = EvaluationResult.Create(ArrayLiteral.CreateWithoutCopy(rest.ToArray(), m_arguments[start].Location, currentEnv.Path));
                }
                else
                {
                    argValue = m_arguments[j].Eval(context, currentEnv, currentFrame);
                }

                if (argValue.IsErrorValue)
                {
                    // Error should have been reported during the evaluation of Arguments[j].
                    return false;
                }

                targetFrame.SetArgument(argPos++, argValue);
            }

            return true;
        }

        internal struct InvocationStatisticHandler
        {
            private static readonly double s_tickFrequency;
            private const long TicksPerMillisecond = 10000;
            private const long TicksPerSecond = TicksPerMillisecond * 1000;

            private readonly bool m_trackingEnabled;
            private long m_startTimeStamp;
            private FunctionStatistic m_statistic;

            static InvocationStatisticHandler()
            {
                if (Stopwatch.IsHighResolution)
                {
                    s_tickFrequency = TicksPerSecond;
                    s_tickFrequency /= Stopwatch.Frequency;
                }
            }

            public InvocationStatisticHandler(bool enabled)
                : this()
            {
                m_trackingEnabled = enabled;
            }

            public void CaptureStatistics(FunctionStatistic statistic)
            {
                m_statistic = statistic;
                if (m_trackingEnabled)
                {
                    // Using Stopwatch.GetTimestamp instead of creating Stopwatch to avoid allocations.
                    m_startTimeStamp = Stopwatch.GetTimestamp();
                }
            }

            public void TrackInvocation(Context context)
            {
                if (m_trackingEnabled && m_statistic != null)
                {
                    var duration = GetElapsedDateTimeTicks();
                    m_statistic.TrackInvocation(context, duration);
                }
            }

            public static InvocationStatisticHandler Create(Context context)
            {
                return new InvocationStatisticHandler(context.TrackMethodInvocationStatistics);
            }

            private long GetElapsedDateTimeTicks()
            {
                // Stopwatch.GetTimestamp returns ticks or not, depending on its resolution.
                // High resolution stopwatch are relies on QueryPErformanceFrequency and
                // the result in this case should be adjusted.
                long rawTicks = Stopwatch.GetTimestamp() - m_startTimeStamp;
                if (Stopwatch.IsHighResolution)
                {
                    return unchecked((long)(rawTicks * s_tickFrequency));
                }

                return rawTicks;
            }
        }

    }

    /// <summary>
    /// Represents Non generic apply expression.
    /// </summary>
    public sealed class NonGenericApplyExpression : ApplyExpression
    {
        /// <nodoc />
        public NonGenericApplyExpression(Expression functor, Expression[] arguments, LineInfo location)
            : base(functor, arguments, location)
        {
        }

        /// <nodoc />
        public NonGenericApplyExpression(DeserializationContext context, LineInfo location)
            : base(ReadExpression(context), ReadExpressions(context), location)
        {
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            Functor.Serialize(writer);
            WriteExpressions(Arguments, writer);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.ApplyExpression;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            var args = "(" + string.Join(", ", Arguments.Select(arg => arg.ToDebugString())) + ")";

            return Functor + args;
        }
    }
}
