// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using Microsoft.Build.Locator;

namespace Microsoft.Build.Prediction
{
    /// <summary>
    /// Utility class that uses MSBuildLocator to find the current MSBuild
    /// binaries and SDKs needed for testing.
    /// </summary>
    internal static class MsBuildEnvironment
    {
        private static readonly object _lock = new object();
        private static bool _alreadyExecuted;

        /// <summary>
        /// Sets up the appdomain. Idempotent and thread-safe, though if different
        /// msBuildHintPaths are passed into multiple calls, the first one to get
        /// the internal lock wins.
        /// </summary>
        /// <param name="msBuildHintPath">
        /// When not null, this path is used to load MSBuild Microsoft.Build.* assemblies if
        /// they have not already been loaded into the appdomain. When null, the fallback
        /// is to look for an installed copy of Visual Studio on Windows.
        /// </param>
        public static void Setup(string msBuildHintPath)
        {
            if (_alreadyExecuted)
            {
                return;
            }

            lock (_lock)
            {
                if (_alreadyExecuted)
                {
                    return;
                }

                if (MSBuildLocator.CanRegister)
                {
                    if (!string.IsNullOrEmpty(msBuildHintPath) && Directory.Exists(msBuildHintPath))
                    {
                        MSBuildLocator.RegisterMSBuildPath(msBuildHintPath);
                    }
                    else
                    {
                        MSBuildLocator.RegisterDefaults();
                    }
                }

                _alreadyExecuted = true;
            }
        }
    }
}
