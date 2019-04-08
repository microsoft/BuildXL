// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
    /// Selector expression like <code>receiver.member</code> when the member was resolved using the semantic model.
    /// </summary>
    /// <remarks>
    /// Unlike regular <see cref="SelectorExpression"/>, this one is used only with semantic name resolution.
    /// The selector in this case is 'resolved' to a known symbol and different evaluation mode is used to get the value.
    /// </remarks>
    public sealed class ResolvedSelectorExpression : SelectorExpressionBase
    {
        private readonly Expression m_selector;

        /// <nodoc />
        public ResolvedSelectorExpression(Expression thisExpression, Expression selector, SymbolAtom name, LineInfo location)
            : base(thisExpression, location)
        {
            Contract.Requires(selector != null, "selector != null");
            Contract.Requires(name.IsValid, "name.IsValid");

            m_selector = selector;
            Selector = name;
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.ResolvedSelectorExpression;

        /// <inheritdoc/>
        public override SymbolAtom Selector { get; }

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return I($"{m_thisExpression.ToDebugString()}.{m_selector.ToDebugString()}");
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            var receiver = m_thisExpression.Eval(context, env, frame);

            if (receiver.IsErrorValue)
            {
                return receiver;
            }

            if (receiver.IsUndefined)
            {
                context.Errors.ReportFailResolveSelectorDueToUndefined(env, m_thisExpression, Selector, Location);
                return EvaluationResult.Error;
            }

            // Resolved expression can work only with module literal and with object literal.
            // It is impossible to get an instance of this type when a selector points to an ambient function.
            // To call an ambient in DScript v2, different ast node is used.
            if (receiver.Value is ModuleLiteral thisModule)
            {
                return m_selector.Eval(context, thisModule, frame);
            }

            if (receiver.Value is ObjectLiteral thisLiteral)
            {
                if (thisLiteral.TryProject(context, Selector, env, context.PredefinedTypes, out EvaluationResult projectionResult, Location))
                {
                    return projectionResult;
                }

                context.Errors.ReportUnexpectedValueType(
                    env,
                    m_thisExpression,
                    receiver, typeof(ObjectLiteral), typeof(ArrayLiteral));
                return EvaluationResult.Error;
            }

            // ThisExpression is not supported.
            context.Errors.ReportUnexpectedValueType(
                    env,
                    m_thisExpression,
                    receiver, typeof(ModuleLiteral), typeof(ObjectLiteral));

            return EvaluationResult.Error;
        }

        /// <nodoc />
        public ResolvedSelectorExpression(DeserializationContext context, LineInfo location)
            : base((Expression)Read(context), location)
        {
            m_selector = (Expression)Read(context);
            Selector = context.Reader.ReadSymbolAtom();
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            ThisExpression.Serialize(writer);
            m_selector.Serialize(writer);
            writer.Write(Selector);
        }
    }
}
