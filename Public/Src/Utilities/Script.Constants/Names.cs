// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.FrontEnd.Script.Constants
{
    /// <nodoc/>
    public static class Names
    {
        /// <nodoc />
        public const string InlineImportFunction = "importFrom";

        /// <nodoc />
        public const string InlineImportFileFunction = "importFile";

        /// <nodoc />
        public const string ToStringFunction = "toString";

        /// <summary>
        /// The name of the decorator that marks entry as an obsolete.
        /// </summary>
        public const string ObsoleteAttributeName = "obsolete";

        /// <summary>
        /// This is only used for internal representation of a root namespace at runtime, and shouldn't leak to users
        /// </summary>
        public const string RuntimeRootNamespaceAlias = "_$";

        /// <summary>
        /// References the root namespace. Similar to 'global::' in C#.
        /// </summary>
        public const string RootNamespace = "$";

        /// <nodoc />
        public const string MergeFunction = "merge";

        /// <nodoc />
        public const string OverrideFunction = "override";

        /// <nodoc />
        public const string OverrideKeyFunction = "overrideKey";

        /// <nodoc/>
        public const string CustomMergeFunctionName = "customMerge";

        /// <summary>
        /// Gets the namespace for ambient string type.
        /// </summary>
        public const string StringNamespace = "String";

        /// <summary>
        /// Gets the namespace for ambient pathATom type.
        /// </summary>
        public const string PathAtomNamespace = "PathAtom";

        /// <summary>
        /// Gets the function name for string interpolation.
        /// </summary>
        public const string InterpolateString = "interpolate";

        /// <summary>
        /// Marker for path atom
        /// </summary>
        public const char PathAtomMarker = '@';

        /// <summary>
        /// Marker for path fragment
        /// </summary>
        public const char PathFragmentMarker = '#';

        /// <summary>
        /// Undefined instance
        /// </summary>
        public const string Undefined = "undefined";

        /// <summary>
        /// name of the Array length property.
        /// </summary>
        public const string ArrayLengthName = "length";

        #region Configuration functions

        /// <nodoc />
        public const string ConfigurationFunctionCall = "config";

        /// <nodoc />
        public const string LegacyModuleConfigurationFunctionCall = "package";

        /// <nodoc />
        public const string ModuleConfigurationFunctionCall = "module";

        #endregion

        #region Glob functions

        /// <nodoc />
        public const string GlobFunction = "glob";

        /// <nodoc />
        public const string GlobRFunction = "globR";

        /// <nodoc />
        public const string GlobRecursivelyFunction = "globRecursively";

        /// <nodoc />
        public const string GlobFoldersFunction = "globFolders";

        #endregion

        #region Qualifiers

        /// <nodoc />
        public const string WithQualifierFunction = "withQualifier";

        /// <nodoc />
        public const string WithQualifierParameter = "newQualifier";

        /// <nodoc />
        public const string CurrentQualifier = "qualifier";

        /// <nodoc />
        public const string BaseQualifierType = "Qualifier";

        #endregion

        #region Templates

        /// <nodoc />
        public const string Template = "template";

        /// <nodoc />
        public const string TemplateReference = "__template_ref_";

        #endregion

        #region path-related factory

        /// <summary>
        /// Path interpolation
        /// </summary>
        public const char PathInterpolationFactory = 'p';

        /// <summary>
        /// Path interpolation
        /// </summary>
        public const char DirectoryInterpolationFactory = 'd';

        /// <summary>
        /// Directory interpolation
        /// </summary>
        public const char FileInterpolationFactory = 'f';

        /// <summary>
        /// Relative path interpolation
        /// </summary>
        public const char RelativePathInterpolationFactory = 'r';

        /// <summary>
        /// Path atom interpolation
        /// </summary>
        public const char PathAtomInterpolationFactory = 'a';

        #endregion

        #region visibility

        /// <nodoc/>
        public const string PublicDecorator = "public";

        #endregion

        #region file names and extensions

        // File names and extensions left here for backwards compatibility
        
        /// <nodoc/>
        [Obsolete("Use PackageConfigDsc")]
        public const string LegacyPackageConfigurationFile = PackageConfigDsc;

        /// <nodoc/>
        [Obsolete("Use DotConfigDotDscExtension")]
        public const string LegacyPackageConfigurationExtension = DotConfigDotDscExtension;

        /// <nodoc/>
        [Obsolete("Use ConfigDsc")]
        public const string LegacyPrimaryConfigurationFile = ConfigDsc;

        /// <nodoc/>
        [Obsolete("Use PackageDsc")]
        public const string LegacyDefaultPackageFile = PackageDsc;

        /// <nodoc/>
        [Obsolete("Use DotBcExtension")]
        public const string PrimaryConfigurationExtension = DotBcExtension;

        /// <nodoc/>
        [Obsolete("Use DotBmExtension")]
        public const string ModuleExtension = DotBmExtension;

        /// <nodoc/>
        [Obsolete("Use DotBpExtension")]
        public const string ProjectExtension = DotBpExtension;

        /// <nodoc/>
        [Obsolete("Use DotBxtExtension")]
        public const string LogicExtension = DotBxtExtension;

        /// <nodoc/>
        [Obsolete("Use DotBlExtension")]
        public const string BuildListExtension = DotBlExtension;

        /// <nodoc/>
        [Obsolete("Use ModuleConfigBm")]
        public const string ModuleConfigurationFile = ModuleConfigBm;

        /// <nodoc/>-
        [Obsolete("Use ConfigBc")]
        public const string PrimaryConfigurationFile = ConfigBc;

        /// <nodoc/>
        public const string DefaultProjectFile = "project" + DotBpExtension;

        /// <summary>
        /// Well-known configuration file names.
        /// </summary>
        public static readonly string[] WellKnownConfigFileNames = new[] {PackageConfigDsc, ModuleConfigDsc, ModuleConfigBm, ConfigDsc, ConfigBc};
        
        /// <summary>
        /// Well-known module configuration file names.
        /// </summary>
        public static readonly string[] WellKnownModuleConfigFileNames = new[] {PackageConfigDsc, ModuleConfigDsc, ModuleConfigBm};

        /// <nodoc/>
        public const string PackageConfigDsc = "package.config" + DotDscExtension;
        
        /// <nodoc/>
        public const string ModuleConfigDsc = "module.config" + DotDscExtension;

        /// <nodoc/>
        public const string ModuleConfigBm = "module.config" + DotBmExtension;

        /// <nodoc/>
        public const string DotConfigDotDscExtension = ".config.dsc";

        /// <nodoc/>
        public const string ConfigDsc = "config" + DotDscExtension;

        /// <nodoc/>
        public const string LegacyUserConfigurationExtension = ".user" + DotDscExtension;

        /// <nodoc/>
        public const string DotDscExtension = ".dsc";
        
        /// <nodoc/>
        public const string PackageDsc = "package" + DotDscExtension;

        /// <nodoc/>
        public const string DotBcExtension = ".bc";

        /// <nodoc/>
        public const string DotBmExtension = ".bm";

        /// <nodoc/>
        public const string DotBpExtension = ".bp";

        /// <nodoc/>
        public const string DotBxtExtension = ".bxt";

        /// <nodoc/>
        public const string DotBlExtension = ".bl";

        /// <nodoc/>-
        public const string ConfigBc = "config" + DotBcExtension;

        #endregion

        #region Packages

        /// <summary>
        /// Name for virtual package induced by the configuration file.
        /// </summary>
        public const string ConfigAsPackageName = "__Config__";

        /// <summary>
        /// Name for another virtual package that has all parsed configuration files.
        /// </summary>
        public const string ConfigModuleName = "__ConfigModule__";

        #endregion Packages

        #region Unsafe

        /// <summary>
        /// Unsafe namespace.
        /// </summary>
        public const string UnsafeNamespace = "Unsafe";

        /// <summary>
        /// Unsafe output file.
        /// </summary>
        public const string UnsafeOutputFile = "outputFile";

        /// <summary>
        /// Unsafe exclusive output directory.
        /// </summary>
        public const string UnsafeExOutputDirectory = "exOutputDirectory";

        #endregion
    }
}
