// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Util;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Literals
{
    /// <summary>
    /// Path literal.
    /// </summary>
    public class PathLiteral : Expression, IConstantExpression, IPathLikeLiteral
    {
        /// <nodoc />
        public AbsolutePath Value { get; }

        /// <inheritdoc />
        object IConstantExpression.Value => Value;

        /// <nodoc />
        public PathLiteral(AbsolutePath value, LineInfo location)
            : base(location)
        {
            Contract.Requires(value.IsValid);
            Value = value;
        }

        /// <nodoc />
        public PathLiteral(DeserializationContext context, LineInfo location)
            : base(location)
        {
            Value = context.Reader.ReadAbsolutePath();
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
        public override SyntaxKind Kind => SyntaxKind.PathLiteral;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return '\'' + PathUtil.NormalizePath(ToDebugString(Value)) + '\'';
        }

        /// <inheritdoc />
        public string ToDisplayString(PathTable table, AbsolutePath currentFolder)
        {
            if (!currentFolder.TryGetRelative(table, Value, out RelativePath result))
            {
                return "{Invalid}";
            }

            return result.ToString(table.StringTable);
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            return EvaluationResult.Create(Value);
        }
    }
}
