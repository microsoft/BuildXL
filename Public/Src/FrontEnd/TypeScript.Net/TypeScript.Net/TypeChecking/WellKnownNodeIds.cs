// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Types;

namespace TypeScript.Net.TypeChecking
{
    /// <summary>
    /// Node ids for well-known types.
    /// </summary>
    internal enum WellKnownNodeIds
    {
        /// <summary>
        /// Identifier for <see cref="AbsolutePathLiteralExpression"/> instances.
        /// </summary>
        AbsolutePathLiteralId = 1,

        /// <summary>
        /// Identifier for <see cref="RelativePathLiteralExpression"/> instances.
        /// </summary>
        RelativePathLiteralId,

        /// <summary>
        /// Identifier for <see cref="PathAtomLiteralExpression"/> instances.
        /// </summary>
        PathAtomLiteralId,

        /// <summary>
        /// Identifier for <see cref="StringIdTemplateLiteralFragment"/> instances.
        /// </summary>
        StringIdTemplateLiteralId,

        /// <summary>
        /// The first 10 ids are reserved for well-known types.
        /// </summary>
        DefaultMinNodeId = 11,
    }
}
