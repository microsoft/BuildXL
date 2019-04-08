// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.FrontEnd.MsBuild
{
    /// <summary>
    /// Well-known environment variable names
    /// </summary>
    public static class BuildEnvironmentConstants
    {
        /// <summary>
        /// Used to inject a unique GUID for MSPDBSRV.exe
        /// TODO: this constant and the related mspdbsrv.exe handling for compiling with /MP need to be moved to all C++ pips instead of just MSBuild pips.
        /// </summary>
        public const string MsPdbSrvEndpointEnvVar = "_MSPDBSRV_ENDPOINT_";

        /// <summary>
        /// Used to provide a unique build GUID to build tools and scripts.
        /// </summary>
        public const string QSessionGuidEnvVar = "Q_SESSION_GUID";

        /// <summary>
        /// Tells the MSBuild engine to use more-efficient async logging.
        /// </summary>
        public const string MsBuildLogAsyncEnvVar = "MSBUILDLOGASYNC";

        /// <summary>
        /// Turn on MsBuild debugging
        /// </summary>
        public const string MsBuildDebug = "MSBUILDDEBUGSCHEDULER";

        /// <summary>
        /// Specified debug path when debugging is on
        /// </summary>
        public const string MsBuildDebugPath = "MSBUILDDEBUGPATH";
    }
}