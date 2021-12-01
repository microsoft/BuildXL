// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.SBOMUtilities
{
    /// <summary>
    /// Set of constants for Component Governance
    /// </summary>
    public static class Constants
    {
        /// <summary>
        /// Environment variable with the bcde-output.json file produced by component detection set in Cloudbuild.
        /// </summary>
        public const string ComponentGovernanceBCDEOutputFilePath = "__COMPONENT_GOVERNANCE_BCDE_OUTPUT_FILE";
    }
}
