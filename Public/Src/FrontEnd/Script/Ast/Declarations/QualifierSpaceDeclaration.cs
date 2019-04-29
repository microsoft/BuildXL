// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.Utilities;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Declarations
{
    /// <summary>
    /// Qualifier space declaration.
    /// </summary>
    public class QualifierSpaceDeclaration : Declaration
    {
        /// <summary>
        /// Qualifier space keyword.
        /// </summary>
        /// <remarks>
        /// This ensures that the AST for manipulation and for evaluation in sync wrt. what the qualifier space is called.
        /// </remarks>
        public SymbolAtom QualifierSpaceKeyword { get; }

        /// <summary>
        /// Qualifier.
        /// </summary>
        public Expression QualifierSpaceExpression { get; }

        /// <nodoc />
        public QualifierSpaceDeclaration(
            SymbolAtom qualifierSpaceKeyword,
            Expression qualifierSpaceExpression,
            LineInfo location)
            : base(DeclarationFlags.None, location)
        {
            Contract.Requires(qualifierSpaceKeyword.IsValid);
            Contract.Requires(qualifierSpaceExpression != null);

            QualifierSpaceKeyword = qualifierSpaceKeyword;
            QualifierSpaceExpression = qualifierSpaceExpression;
        }

        /// <nodoc />
        public QualifierSpaceDeclaration(DeserializationContext context, LineInfo location)
            : base(context, location)
        {
            QualifierSpaceKeyword = ReadSymbolAtom(context);
            QualifierSpaceExpression = ReadExpression(context);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            base.DoSerialize(writer);

            WriteSymbolAtom(QualifierSpaceKeyword, writer);
            Serialize(QualifierSpaceExpression, writer);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.QualifierSpaceDeclaration;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return ToDebugString(QualifierSpaceKeyword) + "(" + QualifierSpaceExpression + ");";
        }
    }
}
