// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Declarations
{
    /// <summary>
    /// Import-export module specifier.
    /// </summary>
    public sealed class ImportOrExportModuleSpecifier : ImportOrExportSpecifier
    {
        /// <summary>
        /// Declare name.
        /// </summary>
        public FullSymbol Name { get; }

        /// <summary>
        /// Name preceding "as" keyword (or invalid when "as" is absent)
        /// </summary>
        public FullSymbol PropertyName { get; }

        /// <nodoc />
        public ImportOrExportModuleSpecifier(FullSymbol name, FullSymbol propertyName, LineInfo location)
            : base(location)
        {
            Contract.Requires(name.IsValid);

            Name = name;
            PropertyName = propertyName;
        }

        /// <nodoc />
        public ImportOrExportModuleSpecifier(DeserializationContext context, LineInfo location)
            : base(context, location)
        {
            Name = ReadFullSymbol(context);
            PropertyName = ReadFullSymbol(context);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            base.DoSerialize(writer);

            WriteFullSymbol(Name, writer);
            WriteFullSymbol(PropertyName, writer);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.ImportOrExportModuleSpecifier;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return (PropertyName.IsValid ? ToDebugString(PropertyName) + " as " : string.Empty) + ToDebugString(Name);
        }
    }
}
