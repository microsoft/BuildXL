// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Expressions
{
    /// <summary>
    /// Index expression.
    /// </summary>
    public class IndexExpression : Expression
    {
        /// <summary>
        /// This expression.
        /// </summary>
        public Expression ThisExpression { get; }

        /// <summary>
        /// Index.
        /// </summary>
        public Expression Index { get; }

        /// <nodoc />
        public IndexExpression(Expression thisExpression, Expression index, LineInfo location)
            : base(location)
        {
            Contract.Requires(thisExpression != null);
            Contract.Requires(index != null);

            ThisExpression = thisExpression;
            Index = index;
        }

        /// <nodoc />
        public IndexExpression(DeserializationContext context, LineInfo location)
            : base(location)
        {
            ThisExpression = ReadExpression(context);
            Index = ReadExpression(context);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            ThisExpression.Serialize(writer);
            Index.Serialize(writer);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.IndexExpression;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return I($"{ThisExpression.ToDebugString()}[{Index.ToDebugString()}]");
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            var e = ThisExpression.Eval(context, env, frame);

            if (e.IsErrorValue)
            {
                return EvaluationResult.Error;
            }

            var indexObj = Index.Eval(context, env, frame);

            if (indexObj.IsErrorValue)
            {
                return EvaluationResult.Error;
            }

            // Extracting local to avoid casting multiple times
            var stringIndexer = indexObj.Value as string;

            if (e.Value is ArrayLiteral arrayLiteral)
            {
                if (indexObj.Value is int index)
                {
                    if (index < 0 || index >= arrayLiteral.Length)
                    {
                        // This behavior is different from the TypeScript one, but this is actually helpful
                        context.Errors.ReportArrayIndexOufOfRange(env, this, index, arrayLiteral.Length);
                        return EvaluationResult.Error;
                    }

                    return arrayLiteral[index];
                }

                // indexer for an array can be a number for getting elements, or string
                if (stringIndexer == null)
                {
                    context.Errors.ReportUnexpectedValueType(env, Index, indexObj, typeof(int), typeof(string));
                    return EvaluationResult.Error;
                }

                // Falling back to object literal case if the indexer is a string.
            }

            if (e.Value is ObjectLiteral objectLiteral)
            {
                if (stringIndexer != null)
                {
                    var selectorId = context.FrontEndContext.StringTable.AddString(stringIndexer);
                    return objectLiteral.GetOrEvalField(context, selectorId, recurs: false, origin: env, location: Location);
                }

                context.Errors.ReportUnexpectedValueType(env, Index, indexObj, typeof(string));
                return EvaluationResult.Error;
            }

            // Indexer in typescript never fails and return undefined.
            // Following this pattern.
            return EvaluationResult.Undefined;
        }
    }
}
