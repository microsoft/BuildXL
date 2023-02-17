// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.Utilities.Core;

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
