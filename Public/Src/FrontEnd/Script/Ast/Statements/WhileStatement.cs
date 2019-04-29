// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using static BuildXL.Utilities.FormattableStringEx;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Statements
{
    /// <summary>
    /// While-statement
    /// </summary>
    public class WhileStatement : Statement
    {
        /// <nodoc />
        public Expression Condition { get; }

        /// <nodoc />
        public Statement Body { get; }

        /// <nodoc />
        public WhileStatement(Expression condition, Statement body, LineInfo location)
            : base(location)
        {
            Contract.Requires(condition != null);
            Contract.Requires(body != null);

            Condition = condition;
            Body = body;
        }

        /// <nodoc />
        public WhileStatement(DeserializationContext context, LineInfo location)
            : base(location)
        {
            Condition = ReadExpression(context);
            Body = Read<Statement>(context);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            Condition.Serialize(writer);
            Body.Serialize(writer);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.WhileStatement;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return I($"while ({Condition.ToDebugString()}){Body.ToDebugString()}");
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            EvaluationResult conditionValue = Condition.Eval(context, env, frame);

            if (conditionValue.IsErrorValue)
            {
                return EvaluationResult.Error;
            }

            // Only unit test can specify MaxloopIterations to override this setting.
            var maxLoopIterations = context.FrontEndHost.FrontEndConfiguration.MaxLoopIterations();

            int iterations = 0;
            while (Expression.IsTruthy(conditionValue))
            {
                if (++iterations > maxLoopIterations)
                {
                    context.Errors.ReportWhileLoopOverflow(env, Location, maxLoopIterations);
                    return EvaluationResult.Error;
                }

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

                conditionValue = Condition.Eval(context, env, frame);

                if (conditionValue.IsErrorValue)
                {
                    return conditionValue;
                }
            }

            return EvaluationResult.Undefined;
        }
    }
}
