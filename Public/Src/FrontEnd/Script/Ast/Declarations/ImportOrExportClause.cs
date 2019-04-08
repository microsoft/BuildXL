// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Declarations
{
    /// <summary>
    ///     Import or export clause.
    /// </summary>
    /// <remarks>
    ///     In case of:
    ///     import/export * from 'f' => namedBinding: NamespaceImport = { name: invalid }
    ///     import * as T.S from 'f' => namedBinding: NamespaceImport = { name: T.S }
    ///     import * as x from 'f' => namedBinding: NamespaceAsVarImport = { name: x }
    ///     import/export { x, y as z, P.Q, T.S as U.W } from 'M' =>
    ///     namedBinding: NamedImports = { elements: [ VarSpecifier{ name: x }, VarSpecifier{ name: y, propertyName: z},
    ///     ModuleSpecifier{ name: P.Q }, ModuleSpecifier{ name: T.S, propertyName: U.W}]}
    ///     export { x, y as z, P.Q, T.S as U.W } => namedBinding: NamedImports = { elements: [ VarSpecifier{ name: x },
    ///     VarSpecifier{ name: y, propertyName: z}, ModuleSpecifier{ name: P.Q }, ModuleSpecifier{ name: T.S, propertyName:
    ///     U.W}]}
    /// </remarks>
    public class ImportOrExportClause : Declaration
    {
        /// <nodoc />
        public ImportOrExportClause(NamedBinding namedBinding, LineInfo location = default(LineInfo))
            : base(DeclarationFlags.None, location)
        {
            Contract.Requires(namedBinding != null);
            NamedBinding = namedBinding;
        }

        /// <nodoc />
        public ImportOrExportClause(DeserializationContext context, LineInfo location)
            : base(context, location)
        {
            NamedBinding = Read<NamedBinding>(context);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            base.DoSerialize(writer);

            Serialize(NamedBinding, writer);
        }

        /// <summary>
        /// Named binding.
        /// </summary>
        public NamedBinding NamedBinding { get; }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.ImportOrExportClause;

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override string ToDebugString()
        {
            var result = NamedBinding.ToString();
            Contract.Assume(result != null);

            return result;
        }
    }
}
