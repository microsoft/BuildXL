// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;
using BuildXL.Utilities.Qualifier;

namespace BuildXL.FrontEnd.Script.RuntimeModel
{
    /// <summary>
    /// Result of parsing a package configuration file.
    /// </summary>
    public sealed class PackageConfigParseResult : SourceFileParseResult
    {
        internal PackageConfigParseResult(
            SourceFile sourceFile,
            FileModuleLiteral module,
            QualifierSpaceId qualifierSpaceId,
            SymbolAtom configurationKeyword)
            : base(sourceFile, module, qualifierSpaceId)
        {
            ConfigurationKeyword = configurationKeyword;
        }

        internal PackageConfigParseResult(SourceFile sourceFile, FileModuleLiteral module, SymbolAtom configurationKeyword)
            : this(sourceFile, module, QualifierSpaceId.Invalid, configurationKeyword)
        {
        }

        internal PackageConfigParseResult(int errorCount)
            : base(errorCount)
        { }

        /// <summary>
        /// The keyword used for configuring the package
        /// </summary>
        public SymbolAtom ConfigurationKeyword { get; }
    }
}
