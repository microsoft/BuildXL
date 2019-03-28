// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.FrontEnd.Sdk
{
    /// <summary>
    /// Set of extension methods for <see cref="FrontEndEngineAbstraction"/> class.
    /// </summary>
    public static class FrontEndEngineAbstractionExtensions
    {
        /// <summary>
        /// Returns <code>true</code> IFF information about both changed and unchanged files is available.
        /// <see cref="FrontEndEngineAbstraction.GetChangedFiles"/>
        /// <see cref="FrontEndEngineAbstraction.GetUnchangedFiles"/>
        /// </summary>
        /// <remarks>
        /// Information about changed files may not always be available, e.g., because engine cache is missing,
        /// engine cache is corrupted, USNJournal is not working, etc.
        /// </remarks>
        public static bool IsSpecChangeInformationAvailable(this FrontEndEngineAbstraction engineAbstraction)
        {
            return
                engineAbstraction.GetChangedFiles() != null &&
                engineAbstraction.GetUnchangedFiles() != null;
        }
    }
}
