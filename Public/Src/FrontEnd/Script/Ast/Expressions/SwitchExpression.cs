// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.Script;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using static BuildXL.Utilities.FormattableStringEx;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Expressions
{
    /// <summary>
    /// Switch expression.
    /// </summary>
    /// <remarks>
    /// This type is similar to ISwitchExpression from typescript AST.
    /// </remarks>
    public class SwitchExpression : Expression
    {
        /// <summary>
        /// Condition.
        /// </summary>
        public Expression Expression { get; }

        /// <summary>
        /// Clauses.
        /// </summary>
        public IReadOnlyList<SwitchExpressionClause> Clauses { get; }

        /// <nodoc />
        public SwitchExpression(
            Expression expression,
            SwitchExpressionClause[] clauses,
            LineInfo location)
            : base(location)
        {
            Contract.Requires(expression != null);
            Contract.Requires(clauses != null);

            Expression = expression;
            Clauses = clauses;
        }

        /// <nodoc />
        public SwitchExpression(DeserializationContext context, LineInfo location)
            : base(location)
        {
            Expression = ReadExpression(context);
            Clauses = ReadArrayOf<SwitchExpressionClause>(context);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            Expression.Serialize(writer);
            WriteArrayOf(Clauses, writer);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.SwitchExpression;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return I($"{Expression.ToDebugString()} switch {{ {string.Join(", ", Clauses.Select<SwitchExpressionClause, string>(clause => clause.ToDebugString()))} }}");
        }

        /// <summary>
        /// This will go over the cases and will evaluate the first clause whose match is equal the the expression.
        /// If no entry matches then undefined will be returned.
        /// </summary>
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            var expression = Expression.Eval(context, env, frame);

            if (expression.IsErrorValue)
            {
                return expression;
            }

            foreach (var clause in Clauses)
            {
                if (clause.IsDefaultFallthrough)
                {
                    return clause.Expression.Eval(context, env, frame);
                }
                else
                {
                    var match = clause.Match.Eval(context, env, frame);
                    if (match.IsErrorValue)
                    {
                        return match;
                    }

                    if (expression.Equals(match))
                    {
                        return clause.Expression.Eval(context, env, frame);
                    }
                }
            }

            return EvaluationResult.Undefined;
        }
    }
}
