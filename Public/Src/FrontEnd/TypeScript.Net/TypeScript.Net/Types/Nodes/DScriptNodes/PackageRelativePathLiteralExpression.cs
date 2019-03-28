// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace TypeScript.Net.Types
{
    /// <summary>
    /// Path relative to a current package.
    /// </summary>
    /// <remarks>
    /// Such literal is created using the following factory method: <code>p`/foo.dsc`</code>.
    /// </remarks>
    public sealed partial class PackageRelativePathLiteralExpression : RelativePathLiteralExpression
    {
        /// <inheritdoc />
        protected override string GetText() => "/" + base.GetText();
    }
}
