// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.FrontEnd.Nuget
{
    /// <summary>
    /// Indicates the download state of a package
    /// </summary>
    public enum NugetProgressState : byte
    {
        /// <summary>
        /// Not yet started downloading
        /// </summary>
        Waiting = 0,

        /// <summary>
        /// Running the download and unpack operations
        /// </summary>
        Running = 1,

        /// <summary>
        /// Started downloading from nuget
        /// </summary>
        DownloadingFromNuget = 2,

        /// <summary>
        /// Succeeded
        /// </summary>
        Succeeded = 3,

        /// <summary>
        /// Failed
        /// </summary>
        Failed = 4,
    }
}
