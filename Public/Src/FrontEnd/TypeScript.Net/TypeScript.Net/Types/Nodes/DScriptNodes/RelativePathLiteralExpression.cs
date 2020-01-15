// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities;
using TypeScript.Net.TypeChecking;

namespace TypeScript.Net.Types
{
    /// <summary>
    /// Relative path created by <code>p`foo.dsc`</code> or <code>r`foo.dsc`</code> factory methods.
    /// </summary>
    public partial class RelativePathLiteralExpression : PathLikeLiteral
    {
        /// <inheritdoc />
        public override int Id => (int)WellKnownNodeIds.RelativePathLiteralId;

        /// <nodoc />
        public RelativePath Path { get; internal set; }

        /// <inheritdoc />
        protected override string GetText() => Path.ToString(PathTable.StringTable, PathFormat.Script);
    }
}
