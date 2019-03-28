// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
