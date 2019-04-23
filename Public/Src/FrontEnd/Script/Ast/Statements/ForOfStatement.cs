// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;
using JetBrains.Annotations;
using static BuildXL.Utilities.FormattableStringEx;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Statements
{
    /// <summary>
    /// For-statement.
    /// </summary>
    public class ForOfStatement : Statement
    {
        /// <nodoc />
        [NotNull]
        public VarStatement Name { get; }

        /// <nodoc />
        [NotNull]
        public Expression Expression { get; }

        /// <nodoc />
        [NotNull]
        public Statement Body { get; }

        /// <nodoc />
        public ForOfStatement(
            VarStatement name,
            Expression expression,
            Statement body,
            LineInfo location)
            : base(location)
        {
            Contract.Requires(name != null);
            Contract.Requires(name.Initializer == null);
            Contract.Requires(expression != null);
            Contract.Requires(body != null);

            Name = name;
            Expression = expression;
            Body = body;
        }

        /// <nodoc />
        public ForOfStatement(DeserializationContext context, LineInfo location)
            : base(location)
        {
            Name = Read<VarStatement>(context);
            Expression = ReadExpression(context);
            Body = Read<Statement>(context);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            Serialize(Name, writer);
            Serialize(Expression, writer);
            Body.Serialize(writer);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.ForOfStatement;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return I($"for ({Name.ToDebugString()} of {Expression.ToDebugString()}){Body.ToDebugString()}");
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            var value = Expression.Eval(context, env, frame);

            if (value.IsErrorValue)
            {
                return EvaluationResult.Error;
            }

            if (!(Expression.Eval(context, env, frame).Value is ArrayLiteral collection))
            {
                context.Errors.ReportUnexpectedValueType(env, Expression, value, new[] { typeof(ArrayLiteral) });
                return EvaluationResult.Error;
            }

            for (int i = 0; i < collection.Length; ++i)
            {
                // Name doesn't have an initializer, so this is basically a no-op. The purpose of calling this
                // is to allow debugger to capture the happening of re-entrance into the for..of loop.
                Name.Eval(context, env, frame);

                frame[Name.Index] = collection[i];
                var b = Body.Eval(context, env, frame);

                // If the body returns `Continue`, then do nothing! `Continue` only affects evaluation within the body, not the while loop.
                if (b.Value == BreakValue.Instance)
                {
                    break;
                }

                if (b.IsErrorValue || frame.ReturnStatementWasEvaluated)
                {
                    // Got the error, or 'return' expression was reached.
                    return b;
                }
            }

            return EvaluationResult.Undefined;
        }
    }
}
