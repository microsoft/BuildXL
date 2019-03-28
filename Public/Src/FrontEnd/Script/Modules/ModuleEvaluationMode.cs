// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;

namespace BuildXL.FrontEnd.Script.Values
{
    /// <summary>
    /// Evaluation modes.
    /// </summary>
    [Flags]
    [SuppressMessage("Microsoft.Naming", "CA1714:FlagsEnumsShouldHavePluralNames")]
    public enum ModuleEvaluationMode
    {
        /// <summary>
        /// Do not traverse Import/export relation.
        /// </summary>
        None = 0,

        /// <summary>
        /// Traverses only local import/export relation, i.e., import from 'a path'.
        /// </summary>
        LocalImportExportTransitive = 1,

        /// <summary>
        /// Traverses only non-local import/export relation, i.e., import from "a package".
        /// </summary>
        NonLocalImportExportTransitive = 2,

        /// <summary>
        /// Traverses all import/export relations.
        /// </summary>
        AllImportExportTransitive = LocalImportExportTransitive | NonLocalImportExportTransitive,
    }
}
