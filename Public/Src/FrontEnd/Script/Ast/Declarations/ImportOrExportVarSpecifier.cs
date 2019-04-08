// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Declarations
{
    /// <summary>
    /// Import-export var specifier.
    /// </summary>
    public sealed class ImportOrExportVarSpecifier : ImportOrExportSpecifier
    {
        /// <summary>
        /// Declare name.
        /// </summary>
        public SymbolAtom Name { get; }

        /// <summary>
        /// Name preceding "as" keyword (or invalid when "as" is absent)
        /// </summary>
        public SymbolAtom PropertyName { get; }

        /// <nodoc />
        public ImportOrExportVarSpecifier(SymbolAtom name, SymbolAtom propertyName, LineInfo location)
            : base(location)
        {
            Contract.Requires(name.IsValid);

            Name = name;
            PropertyName = propertyName;
        }

        /// <nodoc />
        public ImportOrExportVarSpecifier(DeserializationContext context, LineInfo location)
            : base(context, location)
        {
            Name = ReadSymbolAtom(context);
            PropertyName = ReadSymbolAtom(context);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            base.DoSerialize(writer);

            WriteSymbolAtom(Name, writer);
            WriteSymbolAtom(PropertyName, writer);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.ImportOrExportVarSpecifier;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return (PropertyName.IsValid ? ToDebugString(PropertyName) + " as " : string.Empty) + ToDebugString(Name);
        }
    }
}
