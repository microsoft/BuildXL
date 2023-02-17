// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Core.Qualifier;

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
