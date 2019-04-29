// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Expressions;
using TypeScript.Net.Utilities;

namespace BuildXL.FrontEnd.Script.Declarations
{
    /// <summary>
    ///     An export declaration
    /// </summary>
    public class ExportDeclaration : ImportOrExportDeclaration
    {
        /// <summary>
        ///     Constructor for 'export * from literal' or 'export {names} from literal'
        /// </summary>
        public ExportDeclaration(
            ImportOrExportClause importOrExportClause,
            Expression pathSpecifier,
            LineInfo location)
            : base(importOrExportClause, pathSpecifier, DeclarationFlags.None, location)
        {
        }

        /// <summary>
        ///     Constructor for 'export {names}' (without a from clause)
        /// </summary>
        public ExportDeclaration(
            ImportOrExportClause importOrExportClause,
            LineInfo location)
            : this(importOrExportClause, null, location)
        {
        }

        /// <nodoc />
        public ExportDeclaration(DeserializationContext context, LineInfo location)
            : base(context, location)
        {
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.ExportDeclaration;

        /// <inheritdoc />
        public override bool IsExportingValues => true;

        /// <inheritdoc />
        public override bool IsImportingValues => false;

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return "export "
                   + ImportOrExportClause +
                   (PathSpecifier != null ? " from " + PathSpecifier : string.Empty) + ";";
        }
    }
}
