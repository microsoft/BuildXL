// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using JetBrains.Annotations;

namespace TypeScript.Net.ModuleResolution
{
    /// <summary>
    /// Helper class for parsing module specifiers.
    /// </summary>
    public static class ModuleKindParser
    {
        /// <nodoc/>
        public static DscModule? ParseModuleName([NotNull] string moduleName)
        {
            Contract.Requires(!string.IsNullOrEmpty(moduleName));

            if (moduleName[0] == '/')
            {
                // this is package relative module
                return new DscModule(ModuleKind.PackageRelative, moduleName.Substring(1));
            }

            if (moduleName[0] == '.')
            {
                // This is file relative module
                if (moduleName.Length > 2 && moduleName[1] == '/')
                {
                    return new DscModule(ModuleKind.FileRelative, moduleName.Substring(2));
                }

                if (moduleName.IndexOf("../", StringComparison.OrdinalIgnoreCase) == 0)
                {
                    return new DscModule(ModuleKind.FileRelative, moduleName);
                }

                // Report an error?
                return null;
            }

            return new DscModule(ModuleKind.Package, moduleName);
        }
    }

    /// <summary>
    /// Struct that wraps together <see cref="ModuleKind"/> and a parsed module name.
    /// </summary>
    public readonly struct DscModule
    {
        /// <nodoc/>
        public ModuleKind ModuleKind { get; }

        /// <nodoc/>
        [NotNull]
        public string ModuleName { get; }

        /// <nodoc/>
        public DscModule(ModuleKind moduleKind, string moduleName)
        {
            ModuleKind = moduleKind;
            ModuleName = moduleName;
        }
    }
}
