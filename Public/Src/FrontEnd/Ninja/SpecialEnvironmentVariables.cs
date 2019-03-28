// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.FrontEnd.Ninja
{
    internal class SpecialEnvironmentVariables
    {
        /// <summary>
        /// These environment variables change in different CloudBuild sessions
        /// </summary>
        public static string[] CloudBuildEnvironment =
        {
            "Q_SESSION_GUID", "COMPUTERNAME", "USERDOMAIN", "NUGET_PLUGIN_PATHS", "NUGET_CREDENTIALPROVIDERS_PATH", "ARTIFACT_CREDENTIALPROVIDERS_PATH", "TOOLPATH_SIGNINGTOOLS", "CloudStoreRedisCredentialProviderPath"
        };

        /// <summary>
        /// These environment variables change in different CloudBuild sessions
        /// </summary>
        public static string[] PassThroughPrefixes = { "__CLOUDBUILD", "__AZURE", "PackagesCacheStore" };

        /// <summary>
        /// Processes with different values for this environment variable will use different instances of mspdbsrv.exe
        /// </summary>
        public static string MsPdvSrvEndpoint = "_MSPDBSRV_ENDPOINT_";

    }
}
