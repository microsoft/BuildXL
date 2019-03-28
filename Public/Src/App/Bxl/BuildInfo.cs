// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using BuildXL.Utilities;

namespace BuildXL
{
    /// <summary>
    /// Information about the build of bxl.exe.
    /// </summary>
    public sealed class BuildInfo
    {
        /// <summary>
        /// Commit id of the current build
        /// </summary>
        public string CommitId { get; private set; }

        /// <summary>
        /// File version of the build
        /// </summary>
        public string ActualFileVersion { get; private set; }

        /// <summary>
        /// The Build version. The corresponds to the date and patch number specified by the build server.
        /// Ex: 0.20180321.16.0
        /// </summary>
        public string Build { get; private set; }

        /// <summary>
        /// The file version of the build that is incremented by one for developer builds. This can be used to differentiate
        /// developer builds from official builds in watson
        /// </summary>
        public string FileVersionAccountingForDevBuilds
        {
            get
            {
                if (IsDeveloperBuild)
                {
                    Version version;
                    if (Version.TryParse(ActualFileVersion, out version))
                    {
                        return new Version(version.Major, version.Minor, version.Build, version.Revision + 1).ToString(4);
                    }
                }

                return ActualFileVersion;
            }
        }

        /// <summary>
        /// True if it can be determined that the build is a developer build (no commit id stamped from build server)
        /// </summary>
        public bool IsDeveloperBuild => CommitId == "[Developer Build]";

        private BuildInfo()
        {
        }

        /// <summary>
        /// Creates a BuildInfo from the currently running application
        /// </summary>
        public static BuildInfo FromRunningApplication()
        {
            BuildInfo bi = new BuildInfo();

            Assembly entryAssembly = Assembly.GetEntryAssembly();
            if (entryAssembly != null)
            {
                try
                {
                    var fileVersionInfo = FileVersionInfo.GetVersionInfo(AssemblyHelper.GetAssemblyLocation(entryAssembly));
                    if (fileVersionInfo != null)
                    {
                        // The CLR uses the FileVersion of the assembly (not assembly version) for the AppStamp.
                        bi.ActualFileVersion = fileVersionInfo.FileVersion;

                        bi.CommitId = "[" + Branding.SourceVersion + "]";
                        if (!bi.IsDeveloperBuild)
                        {
                            bi.Build = "[" + Branding.Version + "]";
                        }
                    }
                }
                catch (FileNotFoundException)
                {
                }
            }

            return bi;
        }
    }
}
