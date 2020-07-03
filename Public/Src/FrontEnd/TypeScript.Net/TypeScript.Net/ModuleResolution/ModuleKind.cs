// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace TypeScript.Net.ModuleResolution
{
    /// <summary>
    /// Set of different module kinds supported by DScript.
    /// </summary>
    public enum ModuleKind
    {
        /// <summary>
        /// Relative to current dsc file, like importFrom("./foo.dsc");
        /// </summary>
        FileRelative,

        /// <summary>
        /// Relative to current package configuration or config.dsc, like importFrom("/bar.dsc");
        /// </summary>
        PackageRelative,

        /// <summary>
        /// Named package, like importFrom("CommonSdk");
        /// </summary>
        Package,
    }
}
