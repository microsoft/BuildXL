// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using static BuildXL.Utilities.FormattableStringEx;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Expressions
{
    /// <summary>
    /// Module selector expression.
    /// </summary>
    /// <remarks>
    /// Module selector is made mutable for efficiency.
    /// </remarks>
    public class ModuleSelectorExpression : Expression
    {
        /// <nodoc />
        public Expression ThisExpression { get; }

        /// <nodoc />
        public FullSymbol Selector { get; private set; }

        /// <nodoc />
        public ModuleSelectorExpression(Expression thisExpression, FullSymbol selector, LineInfo location)
            : base(location)
        {
            Contract.Requires(thisExpression != null);
            Contract.Requires(selector.IsValid);

            ThisExpression = thisExpression;
            Selector = selector;
        }

        /// <nodoc />
        public ModuleSelectorExpression(DeserializationContext context, LineInfo location)
            : base(location)
        {
            ThisExpression = ReadExpression(context);
            Selector = context.Reader.ReadFullSymbol();
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            ThisExpression.Serialize(writer);
            writer.Write(Selector);
        }

        /// <summary>
        /// Sets a selector.
        /// </summary>
        /// <remarks>
        /// This method is unsafe for multi-threaded. This method is only called by the parser which is single-threaded.
        /// </remarks>
        public void SetSelector(FullSymbol selector)
        {
            Contract.Requires(selector.IsValid);
            Selector = selector;
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.ModuleSelectorExpression;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return I($"{ThisExpression.ToDebugString()}.{ToDebugString(Selector)}");
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            var receiver = ThisExpression.Eval(context, env, frame);

            if (receiver.IsErrorValue)
            {
                return EvaluationResult.Error;
            }

            if (receiver.Value is ObjectLiteral obj)
            {
                if (Selector.GetParent(context.FrontEndContext.SymbolTable) != FullSymbol.Invalid)
                {
                    context.Errors.ReportFailResolveModuleSelector(env, this);
                    return EvaluationResult.Error;
                }

                return obj.GetOrEvalField(
                    context,
                    Selector.GetName(context.FrontEndContext.SymbolTable),
                    recurs: false,
                    origin: env,
                    location: Location);
            }

            context.Errors.ReportUnexpectedValueType(
                env,
                ThisExpression,
                receiver, new[] { typeof(ModuleLiteral), typeof(ObjectLiteral) });

            return EvaluationResult.Error;
        }
    }
}
