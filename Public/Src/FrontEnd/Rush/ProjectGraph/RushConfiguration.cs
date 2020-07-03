// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Rush.ProjectGraph
{
    /// <summary>
    /// A projection of the Rush main configuration with data BuildXL 
    /// cares about, associated with a particular Rush graph
    /// </summary>
    public sealed class RushConfiguration
    {
        /// <nodoc/>
        public RushConfiguration(AbsolutePath commonTempFolder)
        {
            Contract.Requires(commonTempFolder.IsValid);
            CommonTempFolder = commonTempFolder;
        }

        /// <nodoc/>
        public AbsolutePath CommonTempFolder { get; }
    }
}
