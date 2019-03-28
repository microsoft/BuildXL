// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Literals
{
    /// <summary>
    /// String literal that is resolved to path literal.
    /// </summary>
    public class ResolvedStringLiteral : PathLiteral
    {
        /// <nodoc />
        public string OriginalValue { get; }

        /// <nodoc />
        public ResolvedStringLiteral(AbsolutePath resolvedPath, string originalValue, LineInfo location)
            : base(resolvedPath, location)
        {
            Contract.Requires(originalValue != null);
            Contract.Requires(resolvedPath.IsValid);

            OriginalValue = originalValue;
        }

        /// <nodoc />
        public ResolvedStringLiteral(DeserializationContext context, LineInfo location)
            : base(context.Reader.ReadAbsolutePath(), location)
        {
            OriginalValue = context.Reader.ReadString();
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            writer.Write(Value);
            writer.Write(OriginalValue);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.ResolvedStringLiteral;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return OriginalValue;
        }
    }
}
