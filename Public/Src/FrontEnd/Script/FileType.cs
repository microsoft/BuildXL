// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.FrontEnd.Script
{
    /// <summary>
    /// Indicates the type we are parsing
    /// </summary>
    public enum FileType
    {
        /// <summary>
        /// The global config files. These are the config files that define global policies like config.bc
        /// </summary>
        GlobalConfiguration,

        /// <summary>
        /// Module configuraiotn files. These are files that define module configuration like: config.module.bm
        /// </summary>
        ModuleConfiguration,

        /// <summary>
        /// Project files
        /// </summary>
        Project,

        /// <summary>
        /// Parse just an expression
        /// </summary>
        Expression,
    }
}
