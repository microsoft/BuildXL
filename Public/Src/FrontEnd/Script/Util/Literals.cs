// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Constants;

namespace BuildXL.FrontEnd.Script.Util
{
    /// <summary>
    /// Literals commonly used by the parser and the evaluator.
    /// </summary>
    /// Bug #924843: clean up the literals.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1724:TypeNamesShouldNotMatchNamespaces")]
    public sealed class Literals
    {
        private readonly StringTable m_stringTable;

        /// <summary>
        /// Gets the name of the obsolete ambient decorator.
        /// </summary>
        public string ObsoleteString { get; } = Names.ObsoleteAttributeName;

        /// <summary>
        /// Gets the name of the obsolete ambient decorator.
        /// </summary>
        public SymbolAtom Obsolete { get; }

        /// <summary>
        /// Gets the name of the foreignFunction ambient decorator.
        /// </summary>
        public string ForeignFunctionString { get; } = "foreignFunction";

        /// <summary>
        /// Gets the name of the foreignFunction ambient decorator.
        /// </summary>
        public SymbolAtom ForeignFunction { get; }

        // Strings

        /// <summary>
        /// Gets the namespace for ambient string type.
        /// </summary>
        public const string StringNamespace = "String";

        /// <summary>
        /// Gets the function name for string interpolation.
        /// </summary>
        public const string InterpolateString = "interpolate";

        /// <summary>
        /// Gets the name used for the (fake) top-most namespace used in V2
        /// </summary>
        public SymbolAtom RuntimeRootNamespaceSymbol { get; }

        /// <summary>
        /// Gets the name used for referencing the parent template
        /// </summary>
        public SymbolAtom TemplateReference { get; }

        /// <nodoc/>
        public SymbolAtom CustomMergeFunction { get; }

        #region Path

        /// <summary>
        /// Symbol for the Path namespace.
        /// </summary>
        public SymbolAtom PathNamespace { get; }

        /// <summary>
        /// Symbol for the RelativePath namespace.
        /// </summary>
        public SymbolAtom RelativePathNamespace { get; }

        /// <summary>
        /// Symbol for the PathAtom namespace.
        /// </summary>
        public SymbolAtom PathAtomNamespace { get; }

        /// <summary>
        /// Path combine.
        /// </summary>
        public SymbolAtom PathCombine { get; }

        /// <summary>
        /// Path combine paths.
        /// </summary>
        public SymbolAtom PathCombinePaths { get; }

        /// <summary>
        /// Symbol for the interpolate function of path, relative path, and path atom.
        /// </summary>
        public SymbolAtom PathInterpolate { get; }

        /// <summary>
        /// Symbol for the File namespace.
        /// </summary>
        public SymbolAtom FileNamespace { get; }

        /// <summary>
        /// Symbol for the Directory namespace.
        /// </summary>
        public SymbolAtom DirectoryNamespace { get; }

        /// <summary>
        /// Symbol for File.create and Directory.create functions.
        /// </summary>
        public SymbolAtom FileDirCreate { get; }

        /// <summary>
        /// Marker for path atom
        /// </summary>
        public const char PathAtomMarker = '@';

        /// <summary>
        /// Marker for path fragment
        /// </summary>
        public const char PathFragmentMarker = '#';

        /// <summary>
        /// Legacy name of the factory method that is used for creating path instances.
        /// </summary>
        public const char LegacyPathFactoryMethodName = '_';

        #endregion Path

        #region Array

        /// <summary>
        /// Array concat.
        /// </summary>
        public SymbolAtom ArrayConcat { get; }

        #endregion Array

        #region Import

        /// <summary>
        /// Inline import (e.g., importFrom("package"); ).
        /// </summary>
        public SymbolAtom InlineImportFrom { get; }

        #endregion Import

        #region File names and extensions

        /// <summary>
        /// DScript extension.
        /// </summary>
        public PathAtom DotDscExtension { get; }
        
        /// <summary>
        /// Module configuration file extension
        /// </summary>
        public PathAtom DotConfigDotDscExtension { get; }

        /// <summary>
        /// DScript legacy config file.
        /// </summary>
        public PathAtom ConfigDsc { get; }

        /// <summary>
        /// DScript config file.
        /// </summary>
        public PathAtom ConfigBc { get; }

        /// <summary>
        /// DScript legacy package file.
        /// </summary>
        public PathAtom PackageDsc { get; }

        /// <summary>
        /// DScript legacy package configuration file.
        /// </summary>
        public PathAtom PackageConfigDsc { get; }

        /// <summary>
        /// DScript module configuration file.
        /// </summary>
        private PathAtom ModuleConfigBm { get; }
        
        /// <summary>
        /// DScript module configuration file.
        /// </summary>
        private PathAtom ModuleConfigDsc { get; }

        /// <summary>
        /// Returns true if a given candidate is a package config file name (including legacy name).
        /// </summary>
        /// <remarks>
        /// The comparison is case insensitive.
        /// </remarks>
        public bool IsModuleConfigFile(PathAtom candidate)
        {
            return candidate.CaseInsensitiveEquals(m_stringTable, PackageConfigDsc) ||
                   candidate.CaseInsensitiveEquals(m_stringTable, ModuleConfigBm) || 
                   candidate.CaseInsensitiveEquals(m_stringTable, ModuleConfigDsc);
        }

        /// <summary>
        /// Returns true if a given candidate is a package config file name (including legacy name) or a root config file name.
        /// </summary>
        /// <remarks>
        /// The comparison is case insensitive.
        /// </remarks>
        public bool IsWellKnownConfigFile(PathAtom candidate)
        {
            return IsModuleConfigFile(candidate) ||
                candidate.CaseInsensitiveEquals(m_stringTable, ConfigDsc) ||
                candidate.CaseInsensitiveEquals(m_stringTable, ConfigBc);
        }

        /// <summary>
        /// Returns true if a given candidate is a legacy package file name.
        /// </summary>
        /// <remarks>
        /// The comparison is case insensitive.
        /// </remarks>
        public bool IsLegacyPackageFile(PathAtom candidate)
        {
            return candidate.CaseInsensitiveEquals(m_stringTable, PackageDsc);
        }
        #endregion File names and extensions

        #region Configuration

        /// <summary>
        /// BuildXL configuration keyword.
        /// </summary>
        /// <remarks>
        /// A change of configuration keyword in the grammar should be reflected here, and vice versa.
        /// </remarks>
        public SymbolAtom ConfigurationKeyword { get; }

        /// <summary>
        /// Package descriptor keyword.
        /// </summary>
        /// <remarks>
        /// A change of package keyword in the grammar should be reflected here, and vice versa.
        /// Obsolete. Use ModuleKeyword.
        /// </remarks>
        public SymbolAtom LegacyPackageKeyword { get; }

        /// <summary>
        /// Module descriptor keyword.
        /// </summary>
        public SymbolAtom ModuleKeyword { get; }

        /// <nodoc/>
        public SymbolAtom QualifierDeclarationKeyword { get; }

        /// <nodoc/>
        public SymbolAtom WithQualifierKeyword { get; }

        /// <nodoc/>
        public SymbolAtom UndefinedLiteral { get; }

        #endregion

        /// <nodoc />
        public Literals(StringTable stringTable)
        {
            Contract.Requires(stringTable != null);

            m_stringTable = stringTable;

            Obsolete = SymbolAtom.Create(stringTable, ObsoleteString);
            ForeignFunction = SymbolAtom.Create(stringTable, ForeignFunctionString);

            PathNamespace = SymbolAtom.Create(stringTable, "Path");
            RelativePathNamespace = SymbolAtom.Create(stringTable, "RelativePath");
            PathAtomNamespace = SymbolAtom.Create(stringTable, "PathAtom");
            PathCombine = SymbolAtom.Create(stringTable, "combine");
            PathCombinePaths = SymbolAtom.Create(stringTable, "combinePaths");
            PathInterpolate = SymbolAtom.Create(stringTable, Literals.InterpolateString);
            ArrayConcat = SymbolAtom.Create(stringTable, "concat");
            FileNamespace = SymbolAtom.Create(stringTable, "File");
            DirectoryNamespace = SymbolAtom.Create(stringTable, "Directory");
            FileDirCreate = SymbolAtom.Create(stringTable, "fromPath");
            InlineImportFrom = SymbolAtom.Create(stringTable, Names.InlineImportFunction);
            DotDscExtension = PathAtom.Create(stringTable, Names.DotDscExtension);
            ConfigDsc = PathAtom.Create(stringTable, Names.ConfigDsc);
            ConfigBc = PathAtom.Create(stringTable, Names.ConfigBc);
            PackageDsc = PathAtom.Create(stringTable, Names.PackageDsc);
            this.DotConfigDotDscExtension = PathAtom.Create(stringTable, Names.DotConfigDotDscExtension);
            PackageConfigDsc = PathAtom.Create(stringTable, Names.PackageConfigDsc);
            ModuleConfigBm = PathAtom.Create(stringTable, Names.ModuleConfigBm);
            ModuleConfigDsc = PathAtom.Create(stringTable, Names.ModuleConfigDsc);
            ConfigurationKeyword = SymbolAtom.Create(stringTable, Names.ConfigurationFunctionCall);
            LegacyPackageKeyword = SymbolAtom.Create(stringTable, Names.LegacyModuleConfigurationFunctionCall);
            ModuleKeyword = SymbolAtom.Create(stringTable, Names.ModuleConfigurationFunctionCall);
            QualifierDeclarationKeyword = SymbolAtom.Create(stringTable, Names.CurrentQualifier);
            WithQualifierKeyword = SymbolAtom.Create(stringTable, Names.WithQualifierFunction);
            RuntimeRootNamespaceSymbol = SymbolAtom.Create(stringTable, Names.RuntimeRootNamespaceAlias);
            TemplateReference = SymbolAtom.Create(stringTable, Names.TemplateReference);
            UndefinedLiteral = SymbolAtom.Create(stringTable, "undefined");
            CustomMergeFunction = SymbolAtom.Create(stringTable, Names.CustomMergeFunctionName);
        }
    }
}
