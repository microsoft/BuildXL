// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.RuntimeModel;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;

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
