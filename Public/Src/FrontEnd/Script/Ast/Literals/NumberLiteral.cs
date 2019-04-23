// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Globalization;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Literals
{
    /// <summary>
    /// Number literal.
    /// </summary>
    public sealed class NumberLiteral : Expression, IConstantExpression
    {
        /// <summary>
        /// Unboxed literal value.
        /// </summary>
        public int UnboxedValue { get; }

        /// <inheritdoc />
        object IConstantExpression.Value => UnboxedValue;

        /// <nodoc />
        public NumberLiteral(int value, LineInfo location)
            : base(location)
        {
            UnboxedValue = value;
        }

        /// <nodoc />
        public NumberLiteral(BuildXLReader reader, LineInfo location)
            : base(location)
        {
            UnboxedValue = reader.ReadInt32Compact();
        }

        /// <summary>
        /// Boxes an integer
        /// </summary>
        public static EvaluationResult Box(int value)
        {
            return EvaluationResult.Create(BoxedNumber.Box(value));
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.NumberLiteral;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return UnboxedValue.ToString(CultureInfo.InvariantCulture);
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            return EvaluationResult.Create(UnboxedValue);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            writer.WriteCompact(UnboxedValue);
        }
    }
}
