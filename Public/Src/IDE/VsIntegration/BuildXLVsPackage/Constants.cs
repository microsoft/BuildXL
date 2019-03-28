// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.VsPackage
{
    /// <summary>
    /// Types of files that are currently supported by BuildXL package.
    /// </summary>
    internal static class Constants
    {
        /// <summary>
        /// Supported file types
        /// </summary>
        internal const string CSharp = ".cs";
        internal const string CPP = ".cpp";
        internal const string Resource = ".resx";
        internal const string DominoSpec = ".ds";
        internal const string Config = ".config";
        internal const string Lib = ".lib";
        internal const string Png = ".png";
        internal const string Js = ".js";
        internal const string Css = ".css";
        internal const string Html = ".html";
        internal const string Htm = ".htm";
        internal const string Ico = ".ico";

        /// <summary>
        /// Supported project types
        /// </summary>
        internal const string CsProjGuid = "{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}";
        internal const string VcxProjGuid = "{8BC9CEB8-8B4A-11D0-8D11-00A0C91BC942}";

        /// <summary>
        /// Constants used in MSBuild
        /// </summary>
        internal const string DominoTaskEnabled = "DominoTaskEnabled";
        internal const string DominoConfigFile = "DominoConfigFile";
        internal const string DominoExePath = "DominoExePath";
        internal const string DominoValue = "DominoValue";
        internal const string DominoAdditionalOptions = "DominoAdditionalOptions";
        internal const string DominoLogFileDirectory = "DominoLogFileDirectory";
        internal const string DominoMasterStarted = "DominoMasterStarted";
        internal const string DominoPort = "DominoPort";
        internal const string DominoPackageInstalled = "DominoPackageInstalled";
        internal const string DominoInstallDirectory = "DominoInstallDirectory";
        internal const string DominoPackageVersion = "DominoPackageVersion";
        internal const string DominoHostEventName = "DominoHostEventName";
        internal const string NugetMachineInstallRoot = "NugetMachineInstallRoot";
        internal const string DominoPackageDir = "DominoPackageDir";

        internal const string DominoBuildFilterProp = "DominoBuildFilter";
        internal const string DominoSpecFileProp = "DominoSpecFile";

        internal const string DominoSolutionSettingsFileExtension = ".sln.dvs.props";
    }
}
