// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Declarations
{
    /// <summary>
    /// Namespace as var import, see <see cref="TypeScript.Net.Types.ImportClause"/>.
    /// </summary>
    public sealed class NamespaceAsVarImport : NamedBinding
    {
        /// <summary>
        /// Name.
        /// </summary>
        public SymbolAtom Name { get; }

        /// <nodoc />
        public NamespaceAsVarImport(SymbolAtom name, LineInfo location = default(LineInfo))
            : base(location)
        {
            Contract.Requires(name.IsValid);
            Name = name;
        }

        /// <nodoc />
        public NamespaceAsVarImport(DeserializationContext context, LineInfo location)
            : base(context, location)
        {
            Name = ReadSymbolAtom(context);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            base.DoSerialize(writer);

            WriteSymbolAtom(Name, writer);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.NamespaceAsVarImport;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return I($"* as {ToDebugString(Name)}");
        }
    }
}
