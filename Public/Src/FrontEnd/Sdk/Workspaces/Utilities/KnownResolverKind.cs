// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;

namespace BuildXL.FrontEnd.Workspaces.Core
{
    /// <summary>
    /// Known resolver kinds.
    /// </summary>
    public static class KnownResolverKind
    {
        /// <nodoc/>
        public const string SourceResolverKind = "SourceResolver";

        /// <nodoc/>
        public const string DScriptResolverKind = "DScript";

        /// <nodoc/>
        public const string NugetResolverKind = "Nuget";

        /// <nodoc/>
        public const string DownloadResolverKind = "Download";

        /// <nodoc/>
        public const string MsBuildResolverKind = "MsBuild";

        /// <nodoc/>
        public const string RushResolverKind = "Rush";

        /// <nodoc/>
        public const string YarnResolverKind = "Yarn";

        /// <nodoc/>
        public const string CustomJavaScriptResolverKind = "CustomJavaScript";

        /// <nodoc/>
        public const string LageResolverKind = "Lage";

        /// <nodoc/>
        public const string NinjaResolverKind = "Ninja";

        /// <nodoc/>
        public const string NxResolverKind = "Nx";

        /// <nodoc />
        public static readonly string DefaultSourceResolverKind = "DefaultSourceResolver";

        /// <nodoc />
        public static string[] KnownResolvers { get; } =
            {SourceResolverKind, DScriptResolverKind, NugetResolverKind, DefaultSourceResolverKind, DownloadResolverKind, MsBuildResolverKind, NinjaResolverKind, YarnResolverKind, LageResolverKind, CustomJavaScriptResolverKind, NxResolverKind};

        /// <summary>
        /// Returns whether a given string is a valid resolver kind.
        /// </summary>
        public static bool IsValid(string value)
        {
            return
                value == DefaultSourceResolverKind ||
                value == SourceResolverKind ||
                value == DScriptResolverKind ||
                value == NugetResolverKind ||
                value == DownloadResolverKind ||
                value == MsBuildResolverKind ||
                value == NinjaResolverKind ||
                value == RushResolverKind ||
                value == YarnResolverKind ||
                value == LageResolverKind ||
                value == CustomJavaScriptResolverKind ||
                value == NxResolverKind;
        }
    }
}
