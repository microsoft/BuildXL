// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
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
    /// String literal backed by <see cref="StringId"/>.
    /// </summary>
    public sealed class StringIdLiteral : Expression
    {
        private readonly StringId m_value;

        /// <nodoc />
        public StringIdLiteral(StringId value, LineInfo location)
            : base(location)
        {
            Contract.Requires(value != null);
            m_value = value;
        }

        /// <nodoc />
        public StringIdLiteral(BuildXLReader reader, LineInfo location)
            : base(location)
        {
            m_value = reader.ReadStringId();
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.StringLiteral;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            throw new NotSupportedException("Use ToStringShort instead");
        }

        /// <inheritdoc />
        public override string ToStringShort(StringTable stringTable)
        {
            return I($"\"{m_value.ToString(stringTable)}\"");
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            return EvaluationResult.Create(m_value.ToString(context.StringTable));
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            writer.Write(m_value);
        }
    }
}
