// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.RuntimeModel;

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

        /// <summary>
        /// Construcructed workspace.
        /// </summary>
        public Workspace Workspace { get; }

        /// <summary>
        /// Resulting converted module.
        /// </summary>
        public FileModuleLiteral Module => Result;

        /// <nodoc/>
        public ConfigConversionResult(int errorCount)
            : base(errorCount)
        {
        }

        /// <nodoc/>
        public ConfigConversionResult(Workspace workspace, FileModuleLiteral module, SymbolAtom configKeyword)
            : base(module)
        {
            Workspace = workspace;
            ConfigKeyword = configKeyword;
        }
    }
}
