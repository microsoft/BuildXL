// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Literals
{
    /// <summary>
    /// String literal.
    /// </summary>
    public sealed class StringLiteral : Expression, IConstantExpression
    {
        /// <summary>
        /// String value.
        /// </summary>
        public string Value { get; }

        /// <inheritdoc />
        object IConstantExpression.Value => Value;

        /// <nodoc />
        public StringLiteral(string value, LineInfo location)
            : base(location)
        {
            Contract.Requires(value != null);
            Value = value;
        }

        /// <nodoc />
        public StringLiteral(BuildXLReader reader, LineInfo location)
            : base(location)
        {
            Value = reader.ReadString();
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.StringLiteral;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return I($"\"{Value}\"");
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            return EvaluationResult.Create(Value);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            writer.Write(Value);
        }
    }
}
