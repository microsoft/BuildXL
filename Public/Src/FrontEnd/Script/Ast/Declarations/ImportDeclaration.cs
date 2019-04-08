// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Evaluator;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Declarations
{
    /// <summary>
    /// Import declaration.
    /// </summary>
    public sealed class ImportDeclaration : ImportOrExportDeclaration
    {
        /// <nodoc />
        [SuppressMessage("Microsoft.Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods")]
        public Expression Qualifier { get; }

        /// <nodoc />
        public IReadOnlyList<Expression> Decorators { get; }

        /// <nodoc/>
        public ImportDeclaration(ImportOrExportClause importOrExportClause, Expression pathSpecifier, Expression qualifier, IReadOnlyList<Expression> decorators, DeclarationFlags modifier, LineInfo location)
            : base(importOrExportClause, pathSpecifier, modifier, location)
        {
            Contract.Requires(importOrExportClause != null);
            Contract.Requires(pathSpecifier != null);
            Contract.Requires(decorators != null);
            Contract.RequiresForAll(decorators, d => d != null);

            Qualifier = qualifier;
            Decorators = decorators;
        }

        /// <nodoc />
        public ImportDeclaration(DeserializationContext context, LineInfo location)
            : base(Read<ImportOrExportClause>(context), ReadExpression(context), ReadModifier(context.Reader), location)
        {
            Qualifier = ReadExpression(context);
            Decorators = ReadExpressions(context);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            ImportOrExportClause.Serialize(writer);
            PathSpecifier.Serialize(writer);
            WriteModifier(Modifier, writer);

            Serialize(Qualifier, writer);
            WriteExpressions(Decorators, writer);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.ImportDeclaration;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            string decorators = ToDebugString(Decorators);

            return decorators + GetModifierString() + "import "
                   + ImportOrExportClause + " from "
                   + PathSpecifier
                   + (Qualifier != null ? " with " + Qualifier : string.Empty)
                   + ";";
        }


        /// <inheritdoc/>
        public override bool IsExportingValues => (Modifier & DeclarationFlags.Export) != 0;

        /// <inheritdoc/>
        public override bool IsImportingValues => true;
    }
}
