// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Options for journal setup.
    /// </summary>
    public enum SetupJournalOption : byte
    {
        /// <summary>
        /// Journal service is installed if it is not installed or it has a different version.
        /// </summary>
        AsNeeded,

        /// <summary>
        /// Journal service is forcely installed to align with the running BuildXL; the existing one is uninstalled.
        /// </summary>
        ForceUpdate,
    }
}
