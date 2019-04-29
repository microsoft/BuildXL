// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Declarations
{
    /// <summary>
    /// Package declaration.
    /// </summary>
    public class PackageDeclaration : Declaration
    {
        /// <summary>
        /// Package keyword.
        /// </summary>
        /// <remarks>
        /// This ensures that the AST for manipulation and for evaluation in sync wrt. what the package is called.
        /// </remarks>
        public SymbolAtom PackageKeyword { get; }

        /// <nodoc />
        public IReadOnlyList<Expression> PackageExpressions { get; }

        /// <nodoc />
        public PackageDeclaration(
            SymbolAtom packageKeyword,
            Expression packageExpression,
            LineInfo location)
            : this(packageKeyword, new[] { packageExpression }, location)
        {
            Contract.Requires(packageKeyword.IsValid);
            Contract.Requires(packageExpression != null);
        }

        /// <nodoc />
        public PackageDeclaration(
            SymbolAtom packageKeyword,
            IReadOnlyList<Expression> packageExpressions,
            LineInfo location)
            : base(DeclarationFlags.None, location)
        {
            Contract.Requires(packageKeyword.IsValid);
            Contract.Requires(packageExpressions != null);
            Contract.RequiresForAll(packageExpressions, p => p != null);

            PackageKeyword = packageKeyword;
            PackageExpressions = packageExpressions;
        }

        /// <nodoc />
        public PackageDeclaration(DeserializationContext context, LineInfo location)
            : base(context, location)
        {
            PackageKeyword = ReadSymbolAtom(context);
            PackageExpressions = ReadExpressions(context);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            base.DoSerialize(writer);

            WriteSymbolAtom(PackageKeyword, writer);
            WriteExpressions(PackageExpressions, writer);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.PackageDeclaration;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return string.Join(Environment.NewLine, PackageExpressions.Select(p => I($"{ToDebugString(PackageKeyword)}({p})")));
        }
    }
}
