// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.FrontEnd.Script.RuntimeModel;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities.Core;

namespace BuildXL.FrontEnd.Script
{
    /// <summary>
    /// Result of configuration conversion.
    /// </summary>
    public sealed class ConfigConversionResult : ParseResult<FileModuleLiteral>
    {
        /// <summary>
        /// Configuration keyword (e.g., 'config', 'module', or 'package')
        /// </summary>
        public SymbolAtom ConfigKeyword { get; }

        /// <nodoc/>
        public ConfigConversionResult(int errorCount)
            : base(errorCount)
        {
        }

        /// <nodoc/>
        public ConfigConversionResult(FileModuleLiteral module, SymbolAtom configKeyword)
            : base(module)
        {
            ConfigKeyword = configKeyword;
        }
    }
}
