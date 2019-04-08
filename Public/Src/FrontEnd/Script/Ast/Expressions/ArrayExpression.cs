// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Expressions
{
    /// <summary>
    /// Array expression.
    /// </summary>
    // TODO:ST: add a comment that will explain the difference between ArrayExpression and ArrayLiteral!
    public class ArrayExpression : Expression
    {
        private static readonly Expression[] s_empty = CollectionUtilities.EmptyArray<Expression>();

        private IReadOnlyList<Expression> m_values;

        /// <nodoc />
        public IReadOnlyList<Expression> Values => m_values;

        /// <nodoc />
        public ArrayExpression(IReadOnlyList<Expression> values, LineInfo location)
            : base(location)
        {
            Contract.Requires(values != null);
            Contract.RequiresForAll(values, v => v != null);

            m_values = values.Count == 0 ? s_empty : values;
        }

        /// <nodoc />
        public ArrayExpression(DeserializationContext context, LineInfo location)
            : base(location)
        {
            m_values = ReadArrayOf<Expression>(context);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            WriteArrayOf(m_values, writer);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.ArrayExpression;

        /// <summary>
        /// Replaces the values
        /// </summary>
        /// <remarks>
        /// This is a mutating Api. This should not be used by regular interpretor
        /// </remarks>
        public void SetValues(IReadOnlyList<Expression> values)
        {
            Contract.Requires(values != null);
            m_values = values;
        }

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return "[" + string.Join(", ", m_values.Select(v => v.ToDebugString())) + "]";
        }
    }
}
