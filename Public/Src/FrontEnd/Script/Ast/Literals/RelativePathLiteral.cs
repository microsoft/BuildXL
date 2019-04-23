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
    /// Relative path literal.
    /// </summary>
    public sealed class RelativePathLiteral : Expression, IPathLikeLiteral, IConstantExpression
    {
        /// <summary>
        /// Path.
        /// </summary>
        public RelativePath Value { get; }

        /// <inheritdoc />
        object IConstantExpression.Value => Value;

        /// <nodoc />
        public RelativePathLiteral(RelativePath value, LineInfo location)
            : base(location)
        {
            Contract.Requires(value.IsValid);
            Value = value;
        }

        /// <nodoc />
        public RelativePathLiteral(DeserializationContext context, LineInfo location)
            : base(location)
        {
            Value = context.Reader.ReadRelativePath();
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            writer.Write(Value);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.RelativePathLiteral;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return I($"{Constants.Names.RelativePathInterpolationFactory}`{ToDebugString(Value)}`");
        }

        /// <inheritdoc/>
        public string ToDisplayString(PathTable table, AbsolutePath currentFolder)
        {
            return Value.ToString(table.StringTable);
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            return EvaluationResult.Create(Value);
        }
    }
}
