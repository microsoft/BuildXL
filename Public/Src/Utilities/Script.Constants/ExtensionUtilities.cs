// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using static BuildXL.FrontEnd.Script.Constants.Names;

namespace BuildXL.FrontEnd.Script.Constants
{
    /// <summary>
    /// Utility functions to deal with DScript extensions and file names
    /// </summary>
    public static class ExtensionUtilities
    {
        /// <summary>
        /// Valid extensions for project files (including legacy extension)
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public static string[] ScriptProjectExtensions
            => new[] { DotBpExtension, DotBxtExtension, DotDscExtension };

        /// <summary>
        /// All valid extensions (including legacy extension)
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public static string[] ScriptExtensions
            => new[] { DotBcExtension, DotBmExtension, DotBpExtension, DotBxtExtension, DotDscExtension };

        /// <summary>
        /// Whether the extension (e.g. ".txt") is a valid DScript file extension (including legacy)
        /// </summary>
        public static bool IsScriptExtension(string extension)
        {
            if (string.IsNullOrEmpty(extension))
            {
                return false;
            }

            return
                extension.Equals(DotDscExtension, StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(DotBcExtension, StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(DotBmExtension, StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(DotBpExtension, StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(DotBxtExtension, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Whether the extension (e.g. ".txt") is a valid extension for a primary configuration file (including legacy names)
        /// </summary>
        public static bool IsGlobalConfigurationFileExtension(string extension)
        {
            if (string.IsNullOrEmpty(extension))
            {
                return false;
            }

            return extension.Equals(DotBcExtension, StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(DotDscExtension, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Whether this is a config file or not.
        /// </summary>
        public static bool IsGlobalConfigurationFile(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return false;
            }

            if (fileName.Equals(ConfigDsc, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var ext = Path.GetExtension(fileName);
            return string.Equals(ext, DotBcExtension, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Whether the extension (e.g. ".txt") is a DScript legacy extension
        /// </summary>
        public static bool IsLegacyFileExtension(string extension)
        {
            if (string.IsNullOrEmpty(extension))
            {
                return false;
            }

            return extension.Equals(DotDscExtension, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Whether the extension (e.g. ".txt") is a DScript project extension
        /// </summary>
        public static bool IsProjectFileExtension(string extension)
        {
            if (string.IsNullOrEmpty(extension))
            {
                return false;
            }

            return extension.Equals(DotBpExtension, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Whether the file name represents a module configuration file (including legacy name)
        /// </summary>
        public static bool IsModuleConfigurationFile(string filename)
        {
            Contract.Requires(!string.IsNullOrEmpty(filename));

            foreach (var configFileName in WellKnownModuleConfigFileNames)
            {
                if (filename.Equals(configFileName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns true if the <paramref name="fileName"/> is 'module.config.dsc'.
        /// </summary>
        public static bool IsModuleConfigDsc(string fileName) => fileName.Equals(ModuleConfigDsc, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Whether the file name is a valid DScript file and it is not a module nor a primary configuration file
        /// </summary>
        public static bool IsNonConfigurationFile(string filename)
        {
            Contract.Requires(!string.IsNullOrEmpty(filename));

            var extension = Path.GetExtension(filename);
            return IsScriptExtension(extension) && !IsGlobalConfigurationFile(filename) && !IsModuleConfigurationFile(filename);
        }

        /// <summary>
        /// Whether the file name is a valid build list file
        /// </summary>
        public static bool IsBuildListFile(string filename)
        {
            Contract.Requires(!string.IsNullOrEmpty(filename));

            var extension = Path.GetExtension(filename);
            return extension.Equals(DotBlExtension, StringComparison.OrdinalIgnoreCase);
        }
    }
}
