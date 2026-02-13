// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Pips.Reclassification
{
    /// <summary>
    /// The multiple types of reclassification rules supported
    /// </summary>
    /// <remarks>
    /// This enum is used mostly for serialization/deserialization purposes, to identify the concrete type of a reclassification rule.
    /// </remarks>
    public enum ReclassificationRuleType
    {
        /// <summary>
        /// A DScript-configured reclassification rule
        /// </summary>
        /// <remarks>
        /// <see cref="DScriptInternalReclassificationRule"/>
        /// </remarks>
        DScript = 0,
        /// <summary>
        /// A JavaScript package-store-aware reclassification rule
        /// </summary>
        /// <remarks>
        /// <see cref="JavaScriptPackageStoreReclassificationRule"/>
        /// </remarks>
        JavaScriptPackageStore = 1
    }
}
