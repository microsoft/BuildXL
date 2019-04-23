// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Literals
{
    /// <summary>
    /// Boolean literal.
    /// </summary>
    public sealed class BoolLiteral : Expression, IConstantExpression
    {
        /// <summary>
        /// Boxed singleton representing True
        /// </summary>
        private static readonly object s_true = true;

        /// <summary>
        /// Boxed singleton representing False
        /// </summary>
        private static readonly object s_false = false;

        private readonly bool m_unboxedValue;

        /// <inheritdoc />
        object IConstantExpression.Value => m_unboxedValue;

        /// <nodoc />
        internal BoolLiteral(bool value, LineInfo location)
            : base(location)
        {
            m_unboxedValue = value;
        }

        internal BoolLiteral(DeserializationContext context, LineInfo location)
            : base(location)
        {
            m_unboxedValue = context.Reader.ReadBoolean();
        }

        /// <summary>
        /// Creates true.
        /// </summary>
        public static BoolLiteral CreateTrue(LineInfo location)
        {
            return new BoolLiteral(true, location);
        }

        /// <summary>
        /// Creates false.
        /// </summary>
        public static BoolLiteral CreateFalse(LineInfo location)
        {
            return new BoolLiteral(false, location);
        }

        /// <summary>
        /// Boxes a boolean
        /// </summary>
        public static object Box(bool b)
        {
            return b ? s_true : s_false;
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.BoolLiteral;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return m_unboxedValue ? "true" : "false";
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            return EvaluationResult.Create(m_unboxedValue);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            writer.Write(m_unboxedValue);
        }
    }
}
