// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.Threading;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.RuntimeModel.AstBridge;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Expressions.CompositeExpressions
{
    /// <summary>
    /// An array of expressions, where some expressions are spread operators.
    /// </summary>
    /// <remarks>
    /// Lazily evaluates the array and resolves spreads on demand. The result of evaluating this is a regular <see cref="ArrayLiteral"/>.
    /// </remarks>
    public class ArrayLiteralWithSpreads : Expression
    {
        private readonly Expression[] m_elements;
        private readonly int m_spreadExpressionCount;

        private readonly AbsolutePath m_path;

        /// <nodoc/>
        public ArrayLiteralWithSpreads(Expression[] elements, int spreadExpressionCount, LineInfo location, AbsolutePath path)
            : base(location)
        {
            Contract.Requires(elements != null);

            // In tests we're creating the instance of this type with spreadExpressionCount == 0.
            Contract.Requires(spreadExpressionCount >= 0);

            m_elements = elements;
            m_spreadExpressionCount = spreadExpressionCount;
            m_path = path;
        }

        /// <nodoc />
        public ArrayLiteralWithSpreads(DeserializationContext context, LineInfo location)
            : base(location)
        {
            var reader = context.Reader;
            m_path = reader.ReadAbsolutePath();

            int length = reader.ReadInt32Compact();
            m_elements = new Expression[length];

            for (int i = 0; i < length; i++)
            {
                m_elements[i] = ReadExpression(context);
            }

            m_spreadExpressionCount = reader.ReadInt32Compact();
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            writer.Write(m_path);
            writer.WriteCompact(m_elements.Length);
            foreach (var element in m_elements)
            {
                element.Serialize(writer);
            }

            writer.WriteCompact(m_spreadExpressionCount);
        }

        /// <inheritdoc/>
        public override void Accept(Visitor visitor)
        {
            var array = ArrayLiteral.Create(m_elements, Location, m_path);
            visitor.Visit(array);
        }

        /// <inheritdoc/>
        public override SyntaxKind Kind => SyntaxKind.ArrayLiteralWithSpreads;

        /// <inheritdoc/>
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            if (context.TrackMethodInvocationStatistics)
            {
                Interlocked.Increment(ref context.Statistics.ArrayEvaluations);
            }

            // First, evaluating spread expressions to find out what the final size of the array will be.

            // If the element is null in the array then the result of the evaluation was 'undefined'.
            var spreadElements = new ArrayLiteral[m_spreadExpressionCount];
            int spreadElementsIndex = 0;
            int finalLength = m_elements.Length - m_spreadExpressionCount;

            for (int i = 0; i < m_elements.Length; i++)
            {
                var element = m_elements[i];

                if (element.IsSpreadOperator())
                {
                    var result = element.Eval(context, env, frame);

                    // If any of the elements evaluate to an error, we shortcut the evaluation
                    if (result.IsErrorValue)
                    {
                        return result;
                    }

                    // spreads returning undefined add undefined as a single element when
                    // they are not the first element of the array.
                    // E.g [...undefined, 1] --> fail due to undefined
                    // [1, ...undefined, 2] --> [1, undefined, 2]
                    // [...undefined] --> fail due to undefined
                    if (result.IsUndefined)
                    {
                        if (i == 0)
                        {
                            var spread = element as UnaryExpression;
                            return ReportError(context, spread);
                        }

                        finalLength++;
                    }

                    if (result.Value is ArrayLiteral arrayResult)
                    {
                        spreadElements[spreadElementsIndex] = arrayResult;

                        spreadElementsIndex++;
                        finalLength += arrayResult.Length;
                    }
                    else
                    {
                        context.Errors.ReportUnexpectedValueType(
                            env,
                            this,
                            result,
                            typeof(ArrayLiteral));
                        return EvaluationResult.Error;
                    }
                }
            }

            // The final result of the evaluation
            var evaluatedArray = new EvaluationResult[finalLength];

            spreadElementsIndex = 0;
            int currentPosition = 0;
            for (int i = 0; i < m_elements.Length; i++)
            {
                var element = m_elements[i];

                if (element.IsSpreadOperator())
                {
                    var evaluatedArrayElement = spreadElements[spreadElementsIndex];
                    if (evaluatedArrayElement == null)
                    {
                        // Null means that the result was undefined.
                        evaluatedArray[currentPosition] = EvaluationResult.Undefined;
                    }
                    else
                    {
                        evaluatedArrayElement.Copy(0, evaluatedArray, currentPosition, evaluatedArrayElement.Length);
                        currentPosition += evaluatedArrayElement.Length;
                    }

                    spreadElementsIndex++;
                }
                else
                {
                    var result = element.Eval(context, env, frame);

                    if (result.IsErrorValue)
                    {
                        return result;
                    }

                    evaluatedArray[currentPosition] = result;
                    currentPosition++;
                }
            }

            return EvaluationResult.Create(ArrayLiteral.CreateWithoutCopy(evaluatedArray, Location, m_path));
        }

        private EvaluationResult ReportError(Context context, UnaryExpression spread)
        {
            var location = UniversalLocation.FromLineInfo(spread.Location, m_path, context.PathTable);
            context.Logger.ReportFailResolveSelectorDueToUndefined(
                context.LoggingContext,
                location.AsLoggingLocation(),
                spread.Expression.ToDisplayString(context),
                spread.ToDisplayString(context),
                context.GetStackTraceAsString(location));

            return EvaluationResult.Error;
        }
    }
}
