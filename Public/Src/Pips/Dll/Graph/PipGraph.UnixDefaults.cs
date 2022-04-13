// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Interop.Unix;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Pips.Graph
{
    partial class PipGraph
    {
        /// <nodoc />
        public class UnixDefaults
        {
            private static readonly SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer> s_emptySealContents
                = CollectionUtilities.EmptySortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>(OrdinalFileArtifactComparer.Instance);

            private static readonly SortedReadOnlyArray<DirectoryArtifact, OrdinalDirectoryArtifactComparer> s_emptyOutputDirectoryContents
                = CollectionUtilities.EmptySortedReadOnlyArray<DirectoryArtifact, OrdinalDirectoryArtifactComparer>(OrdinalDirectoryArtifactComparer.Instance);

            private class DefaultSourceSealDirectories
            {
                public readonly bool IsValid;
                public readonly DirectoryArtifact[] Directories;

                public DefaultSourceSealDirectories(DirectoryArtifact[] dirs)
                {
                    IsValid     = dirs.All(d => d.IsValid);
                    Directories = dirs;
                }
            }

            private readonly PipProvenance m_provenance;
            private readonly AbsolutePath[] m_sourceSealDirectoryPaths;
            private readonly Lazy<DefaultSourceSealDirectories> m_lazySourceSealDirectories;
            private readonly FileArtifact[] m_untrackedFiles;
            private readonly DirectoryArtifact[] m_untrackedDirectories;

            /// <nodoc />
            public UnixDefaults(PathTable pathTable, IMutablePipGraph pipGraph)
            {
                m_provenance = new PipProvenance(
                    0,
                    ModuleId.Invalid,
                    StringId.Invalid,
                    FullSymbol.Invalid,
                    LocationData.Invalid,
                    QualifierId.Unqualified,
                    PipData.Invalid);

                m_sourceSealDirectoryPaths = Enumerable
                    .Empty<string>()
                    .Concat(IfMacOs(
                        MacPaths.Applications,
                        MacPaths.Library,
                        MacPaths.UserProvisioning))
                    .Select(p => AbsolutePath.Create(pathTable, p))
                    .ToArray();

                // Sealed Source inputs
                // (using Lazy so that these directories are sealed and added to the graph only if explicitly requested by a process)
                m_lazySourceSealDirectories = Lazy.Create(() =>
                    new DefaultSourceSealDirectories(m_sourceSealDirectoryPaths.Select(p => GetSourceSeal(pipGraph, p)).ToArray()));

                // TODO: try not to untrack so many paths
                m_untrackedFiles =
                    new[]
                    {
                        UnixPaths.Etc,
                        UnixPaths.EtcMasterPasswd,
                        UnixPaths.EtcLocalTime,
                        // login.keychain is created by the OS the first time any process invokes an OS API that references the keychain.
                        // Untracked because build state will not be stored there and code signing will fail if required certs are in the keychain
                        UnixPaths.TmpDir,
                        UnixPaths.EtcOsRelease
                    }
                    .Concat(IfMacOs(
                        MacPaths.UserKeyChainsDb,
                        MacPaths.UserKeyChains,
                        MacPaths.UserCFTextEncoding))
                    .Select(p => FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, p)))
                    .ToArray();

                m_untrackedDirectories =
                    new[]
                    {
                        UnixPaths.Bin,
                        UnixPaths.Dev,
                        UnixPaths.Etc, // /etc could be a folder or a directory symlink, hence should be untracked as both a path and a scope
                        UnixPaths.Private,
                        UnixPaths.Proc,
                        UnixPaths.Sbin,
                        UnixPaths.Sys,
                        UnixPaths.UsrBin,
                        UnixPaths.UsrInclude,
                        UnixPaths.UsrLibexec,
                        UnixPaths.UsrShare,
                        UnixPaths.UsrStandalone,
                        UnixPaths.UsrSbin,
                        UnixPaths.Var,
                        // it's important to untrack /usr/lib instead of creating a sealed source directory
                        //   - the set of dynamically loaded libraries during an execution of a process is 
                        //     not necessarily deterministic, i.e., when the same process---which itself is
                        //     deterministic---is executed multiple times on same inputs, the set of 
                        //     dynamically loaded libraries is not necessarily going to stay the same.
                        UnixPaths.UsrLib,
                        UnixPaths.LibLinuxGnu,
                        UnixPaths.Lib64,
                        UnixPaths.Run,
                    }
                    .Concat(IfMacOs(
                        MacPaths.AppleInternal,
                        MacPaths.SystemLibrary,
                        MacPaths.UserPreferences))
                    .Select(p => DirectoryArtifact.CreateWithZeroPartialSealId(pathTable, p))
                    .ToArray();
            }

            private static IEnumerable<string> IfMacOs(params string[] elems) => If(OperatingSystemHelper.IsMacOS, elems);
            private static IEnumerable<string> If(bool condition, params string[] elems) => condition
                ? elems
                : Enumerable.Empty<string>();

            /// <summary>
            /// Augments the processBuilder with the OS dependencies.
            /// </summary>
            /// <param name="processBuilder">builder to use</param>
            /// <param name="untrackInsteadSourceSeal">when true, directories that are meant to be source sealed are untracked instead</param>
            public bool ProcessDefaults(ProcessBuilder processBuilder, bool untrackInsteadSourceSeal = false)
            {
                if (processBuilder.Options.HasFlag(Operations.Process.Options.DependsOnCurrentOs))
                {
                    // process source seal directories: either source seal them or untrack them, depending on 'untrackInsteadSourceSeal'
                    if (untrackInsteadSourceSeal)
                    {
                        foreach (var sourceSealDirPath in m_sourceSealDirectoryPaths)
                        {
                            processBuilder.AddUntrackedDirectoryScope(DirectoryArtifact.CreateWithZeroPartialSealId(sourceSealDirPath));
                        }
                    }
                    else
                    {
                        var defaultSourceSealDirs = m_lazySourceSealDirectories.Value;
                        if (!defaultSourceSealDirs.IsValid)
                        {
                            return false;
                        }

                        foreach (var inputDirectory in defaultSourceSealDirs.Directories)
                        {
                            processBuilder.AddInputDirectory(inputDirectory);
                        }
                    }

                    // add untracked files
                    foreach (var untrackedFile in m_untrackedFiles)
                    {
                        processBuilder.AddUntrackedFile(untrackedFile);
                    }

                    // add untracked directories
                    foreach (var untrackedDirectory in m_untrackedDirectories)
                    {
                        processBuilder.AddUntrackedDirectoryScope(untrackedDirectory);
                    }
                }

                return true;
            }

            private DirectoryArtifact GetSourceSeal(IMutablePipGraph pipGraph, AbsolutePath path)
            {
                var sealDirectory = new SealDirectory(
                    path,
                    contents: s_emptySealContents,
                    outputDirectoryContents: s_emptyOutputDirectoryContents,
                    kind: SealDirectoryKind.SourceAllDirectories,
                    provenance: m_provenance,
                    tags: ReadOnlyArray<StringId>.Empty,
                    patterns: ReadOnlyArray<StringId>.Empty,
                    scrub: false);

                return pipGraph.AddSealDirectory(sealDirectory, default);
            }
        }
    }
}
