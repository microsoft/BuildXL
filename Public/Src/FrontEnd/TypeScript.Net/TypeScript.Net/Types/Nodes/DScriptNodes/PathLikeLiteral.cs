// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using static BuildXL.Utilities.FormattableStringEx;

namespace TypeScript.Net.Types
{
    /// <summary>
    /// Base class for path-like literals like p``, d``, f`` etc.
    /// </summary>
    public abstract class PathLikeLiteral : Literal, IPathLikeLiteralExpression, IIdentifier
    {
        /// <nodoc/>
        public LiteralExpressionKind LiteralKind
        {
            get { return (LiteralExpressionKind)m_reservedByte; }
            set { m_reservedByte = (byte)value; }
        }

        /// <inheritdoc/>
        SyntaxKind IIdentifier.OriginalKeywordKind { get { return SyntaxKind.Unknown; } set { throw new NotSupportedException(); } }

        /// <inheritdoc/>
        public override string Text { get => I($"{GetText()}"); set { throw new NotSupportedException("Can't change 'Text' property."); } }

        /// <nodoc/>
        protected abstract string GetText();

        /// <nodoc/>
        public override string ToDisplayString()
        {
            return Text;
        }

        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitPathLikeLiteral(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitPathLikeLiteral(this);
        }
    }
}
