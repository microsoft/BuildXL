// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
