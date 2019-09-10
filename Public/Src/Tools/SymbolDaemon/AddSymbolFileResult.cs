// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Tool.SymbolDaemon
{
    /// <summary>
    /// Result of a <see cref="Microsoft.VisualStudio.Services.Symbol.App.Core.ISymbolServiceClient.CreateRequestDebugEntriesAsync"/>
    /// operation (adding a single DebugEntry to a symbol request).
    /// </summary>
    /// <remarks>
    /// These are our own statuses that are based on the fact whether we had to upload a blob or not
    /// (the only 'result' value returned by Artifact Services APIs is <see cref="Microsoft.VisualStudio.Services.Symbol.WebApi.DebugEntryStatus"/>).
    /// </remarks>
    public enum AddDebugEntryResult
    {
        /// <summary>
        /// Indicates that the server already had the file and completed the request using the only supplied metadata.
        /// </summary>
        Associated,

        /// <summary>
        /// Indicates that the file was missing from the server and it had to be uploaded prior completing the request.
        /// </summary>
        UploadedAndAssociated
    }
}