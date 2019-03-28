// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Declarations
{
    /// <summary>
    /// Named import or exports.
    /// </summary>
    public sealed class NamedImportsOrExports : NamedBinding
    {
        /// <summary>
        /// Import-export specifiers.
        /// </summary>
        public IReadOnlyList<ImportOrExportSpecifier> Elements { get; }

        /// <nodoc />
        public NamedImportsOrExports(IReadOnlyList<ImportOrExportSpecifier> elements, LineInfo location)
            : base(location)
        {
            Contract.Requires(elements != null);

            // 'elements' could be empty.
            // This is possible in some V2 scenarios.
            Contract.RequiresForAll(elements, e => e != null);

            Elements = elements;
        }

        /// <nodoc />
        public NamedImportsOrExports(DeserializationContext context, LineInfo location)
            : base(context, location)
        {
            Elements = ReadArrayOf<ImportOrExportSpecifier>(context);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            base.DoSerialize(writer);

            WriteArrayOf(Elements, writer);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.NamedImportsOrExports;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            var elements = string.Join(", ", Elements.Select(e => e.ToDebugString()));
            return I($"{{{elements}}}");
        }
    }
}
