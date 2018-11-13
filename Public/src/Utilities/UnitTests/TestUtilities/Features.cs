// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Test.BuildXL.TestUtilities
{
    /// <summary>
    /// Names of features for tagging tests
    /// </summary>
    public static class Features
    {
#pragma warning disable 1591
        public const string Feature = "Feature";

        // Note - These are meant to tag features that get tested across many dimensions and have associated tests in
        // many locations. It is not meant for isolated features that don't need to consider cross cutting compatibility.
        // For example, there need not be a category for the pip filter parser because that is quite isolated.
        public const string AbsentFileProbe = "AbsentFileProbe";
        public const string ExistingFileProbe = "ExistingFileProbe";
        public const string CopyFilePip = "CopyFilePip";
        public const string DirectoryEnumeration = "DirectoryEnumeration";
        public const string DirectoryProbe = "DirectoryProbe";
        public const string DirectoryTranslation = "DirectoryTranslation";
        public const string Filtering = "Filtering";
        public const string GraphFileSystem = "GraphFileSystem";
        public const string IncrementalScheduling = "IncrementalScheduling";
        public const string IpcPip = "IpcPip";
        public const string LazyOutputMaterialization = "LazyOutputMaterialization";
        public const string Mount = "Mount";
        public const string OpaqueDirectory = "OpaqueDirectory";
        public const string SharedOpaqueDirectory = "SharedOpaqueDirectory";
        public const string RewrittenFile = "RewrittenFile";
        public const string SealedDirectory = "SealedDirectory";
        public const string SealedSourceDirectory = "SealedSourceDirectory";
        public const string SearchPath = "SearchPath"; // SearchPathEnumeration
        public const string ServicePip = "ServicePip";
        public const string Symlink = "Symlink";
        public const string TempDirectory = "TempDirectory";
        public const string OptionalOutput = "OptionalOutput";
        public const string NonStandardOptions = "NonStandardOptions"; // Command line options that can affect how processes are run
        public const string UndeclaredAccess = "UndeclaredAccess";
        public const string UntrackedAccess = "UntrackedAccess";
        public const string Whitelist = "Whitelist";
        public const string WriteFilePip = "WriteFilePip";
#pragma warning restore 1591
    }
}
