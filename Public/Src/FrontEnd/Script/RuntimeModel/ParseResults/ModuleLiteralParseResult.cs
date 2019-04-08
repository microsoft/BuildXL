// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Values;

namespace BuildXL.FrontEnd.Script.RuntimeModel
{
    /// <summary>
    /// Result of parsing an expression.
    /// </summary>
    public sealed class ModuleLiteralParseResult : ParseResult<FileModuleLiteral>
    {
        internal ModuleLiteralParseResult(FileModuleLiteral moduleLiteral)
            : base(moduleLiteral)
        { }

        internal ModuleLiteralParseResult(int errorCount)
            : base(errorCount)
        { }
    }
}
