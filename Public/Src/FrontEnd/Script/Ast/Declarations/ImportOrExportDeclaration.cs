// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.Utilities;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Declarations
{
    /// <summary>
    ///     An import or export declaration
    /// </summary>
    public abstract class ImportOrExportDeclaration : Declaration
    {
        /// <summary>
        ///     Path specifier for module (the 'from PathSpecifier' clause).
        /// </summary>
        /// <remarks>May be null, indicating the path specifier is not there (e.g. 'export {names}')</remarks>
        public readonly Expression PathSpecifier;

        /// <nodoc />
        protected ImportOrExportDeclaration(
            ImportOrExportClause importOrExportClause,
            Expression pathSpecifier,
            DeclarationFlags modifier,
            LineInfo location)
            : base(modifier, location)
        {
            Contract.Requires(importOrExportClause != null);

            ImportOrExportClause = importOrExportClause;
            PathSpecifier = pathSpecifier;
        }

        /// <nodoc />
        protected ImportOrExportDeclaration(DeserializationContext context, LineInfo location)
            : base(context, location)
        {
            ImportOrExportClause = Read<ImportOrExportClause>(context);
            PathSpecifier = ReadExpression(context);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            base.DoSerialize(writer);

            Serialize(ImportOrExportClause, writer);
            Serialize(PathSpecifier, writer);
        }

        /// <summary>
        ///     Named imports or exports. See <see cref="ImportOrExportClause" /> for details.
        /// </summary>
        public ImportOrExportClause ImportOrExportClause { get; }

        /// <summary>
        ///     Whether this is an export declaration or an import declaration with an 'export' modifier
        /// </summary>
        public abstract bool IsExportingValues { get; }

        /// <summary>
        ///     Whether this is an import declaration
        /// </summary>
        public abstract bool IsImportingValues { get; }
    }
}
