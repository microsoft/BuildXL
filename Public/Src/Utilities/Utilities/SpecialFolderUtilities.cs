// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Helper method for Environment.GetFolder functionality on .NET Core
    /// </summary>
    public static partial class SpecialFolderUtilities
    {
        // .NET Core doesn't currently have APIs to get the "special" folders (ie the ones defined in the Environment.SpecialFolder enum)
        //  The code here is mostly copied out of the .NET reference source for this functionality

        /// <summary>
        /// Precomputed SpecialFolder paths. If set, <see cref="GetFolderPath"/> will first try to get a path from this dictionary,
        /// before falling back to using <see cref="Environment.GetFolderPath(System.Environment.SpecialFolder)"/>.
        /// </summary>
        private static IReadOnlyDictionary<string, string> s_envVars;

        /// <summary>
        /// Gets the path to the system special folder that is identified by the specified enumeration.
        /// </summary>
        /// <param name="folder">An enumerated constant that identifies a system special folder.</param>
        /// <param name="option">Specifies options to use for accessing a special folder.</param>
        /// <returns>The path to the specified system special folder, if that folder physically exists
        /// on your computer; otherwise, an empty string ("")</returns>
        public static string GetFolderPath(Environment.SpecialFolder folder, Environment.SpecialFolderOption option = Environment.SpecialFolderOption.None)
        {
            // check if there is profile redirect
            if (s_envVars != null)
            {
                string envVar = null;
                switch (folder)
                {
                    case Environment.SpecialFolder.InternetCache:
                        envVar = "INTERNETCACHE";
                        break;
                    case Environment.SpecialFolder.History:
                        envVar = "INTERNETHISTORY";
                        break;
                    case Environment.SpecialFolder.ApplicationData:
                        envVar = "APPDATA";
                        break;
                    case Environment.SpecialFolder.LocalApplicationData:
                        envVar = "LOCALAPPDATA";
                        break;
                    case Environment.SpecialFolder.UserProfile:
                        envVar = "USERPROFILE";
                        break;
                    case Environment.SpecialFolder.Cookies:
                        envVar = "INETCOOKIES";
                        break;
                }

                if (envVar != null && s_envVars.TryGetValue(envVar, out var path))
                {
                    return path;
                }
            }

            // there is either no profile redirect or we could not find the corresponding path in the provided dictionary
            return Environment.GetFolderPath(folder, option);
        }

        /// <summary>
        /// Gets the fully qualified path of the system directory.
        /// </summary>
        public static string SystemDirectory => Environment.SystemDirectory;

        /// <summary>
        /// Initialize the special folder paths dictionary.
        /// </summary>
        /// <remarks>The dictionary must contain the env variables computed in RedirectUserProfileDirectory</remarks>
        public static void InitRedirectedUserProfilePaths(IReadOnlyDictionary<string, string> envVariables)
        {
            s_envVars = envVariables;
        }

        /// <summary>
        /// Returns the precomputed special folder paths for the redirected user profile.
        /// </summary>
        /// <remarks>The dictionary contains the list of env variables that define the redirected user profile.</remarks>
        public static IReadOnlyDictionary<string, string> GetRedirectedUserProfilePaths()
        {
            return s_envVars;
        }
    }
}
