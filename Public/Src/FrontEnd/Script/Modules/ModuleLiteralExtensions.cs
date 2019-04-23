// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Script.Values
{
    /// <summary>
    /// Set of extension methods for <see cref="ModuleLiteral"/> type.
    /// </summary>
    internal static class ModuleLiteralExtensions
    {
        public static AbsolutePath GetPath(this ModuleLiteral moduleLiteral, ImmutableContextBase context)
        {
            return moduleLiteral.Path.IsValid ? moduleLiteral.Path : context.LastActiveUsedPath;
        }
    }
}
