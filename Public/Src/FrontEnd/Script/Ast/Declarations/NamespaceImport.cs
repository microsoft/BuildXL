// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Declarations
{
    /// <summary>
    /// Namespace import, see <see cref="TypeScript.Net.Types.ImportClause"/>.
    /// </summary>
    public sealed class NamespaceImport : NamedBinding
    {
        /// <summary>
        /// Name. Can be invalid for 'export * from' statements.
        /// </summary>
        public FullSymbol Name { get; }

        /// <nodoc />
        public NamespaceImport(FullSymbol name, LineInfo location)
            : base(location)
        {
            Name = name;
        }

        /// <nodoc />
        public NamespaceImport(DeserializationContext context, LineInfo location)
            : base(context, location)
        {
            Name = ReadFullSymbol(context);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            base.DoSerialize(writer);

            WriteFullSymbol(Name, writer);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.NamespaceImport;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return "*" + (Name.IsValid ? " as " + ToDebugString(Name) : string.Empty);
        }
    }
}
