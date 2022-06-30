// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Pips.Graph
{
    partial class PipGraph
    {
        /// <nodoc />
        public class WindowsOsDefaults : OsDefaults
        {
            private readonly DirectoryArtifact m_applicationDataPath;
            private readonly DirectoryArtifact m_localApplicationDataPath;

            private readonly DirectoryArtifact m_commonApplicationDataPath;

            /// <inheritdoc/>
            public DirectoryArtifact[] UntrackedDirectories { get; }

            /// <inheritdoc/>
            public FileArtifact[] UntrackedFiles => CollectionUtilities.EmptyArray<FileArtifact>();
            
            /// <nodoc />
            public WindowsOsDefaults(PathTable pathTable)
            {
                UntrackedDirectories =
                    new[]
                    {
                        GetSpecialFolder(pathTable, Environment.SpecialFolder.Windows),
                        GetSpecialFolder(pathTable, Environment.SpecialFolder.InternetCache),
                        GetSpecialFolder(pathTable, Environment.SpecialFolder.History),

                        GetSpecialFolder(pathTable, Environment.SpecialFolder.ProgramFiles, "Windows Defender"),
                        GetSpecialFolder(pathTable, Environment.SpecialFolder.ProgramFilesX86, "Windows Defender"),
                        GetSpecialFolder(pathTable, Environment.SpecialFolder.CommonApplicationData, "Microsoft", "Windows Defender"),
                    };

                m_applicationDataPath = GetSpecialFolder(pathTable, Environment.SpecialFolder.ApplicationData);
                m_localApplicationDataPath = GetSpecialFolder(pathTable, Environment.SpecialFolder.LocalApplicationData);

                m_commonApplicationDataPath = GetSpecialFolder(pathTable, Environment.SpecialFolder.CommonApplicationData);
            }

            /// <summary>
            /// Augments the processBuilder with the OS dependencies
            /// </summary>
            public bool ProcessDefaults(ProcessBuilder processBuilder)
            {
                if ((processBuilder.Options & Process.Options.DependsOnCurrentOs) != 0)
                {
                    foreach (var untrackedDirectory in UntrackedDirectories)
                    {
                        AddUntrackedScopeIfValid(untrackedDirectory, processBuilder);
                    }
                }

                if ((processBuilder.Options & Process.Options.DependsOnWindowsAppData) != 0)
                {
                    AddUntrackedScopeIfValid(m_applicationDataPath, processBuilder);
                    AddUntrackedScopeIfValid(m_localApplicationDataPath, processBuilder);
                }

                if ((processBuilder.Options & Process.Options.DependsOnWindowsProgramData) != 0)
                {
                    AddUntrackedScopeIfValid(m_commonApplicationDataPath, processBuilder);
                }

                return true;
            }

            /// <summary>
            /// Some Windows Special Folders cannot be retrieved based on the state of the user profile. Only add them
            /// if they were successfull retrieved.
            /// </summary>
            private void AddUntrackedScopeIfValid(DirectoryArtifact directoryArtifact, ProcessBuilder processBuilder)
            {
                if (directoryArtifact.IsValid)
                {
                    processBuilder.AddUntrackedDirectoryScope(directoryArtifact);
                }
            }

            private static DirectoryArtifact GetSpecialFolder(PathTable pathTable, Environment.SpecialFolder specialFolder, params string[] subFolders)
            {
                // GetFolderPath will return empty paths for special folders that don't exist in the current user profile.
                // Return DirectoryArtifact.Invalid for those folders so they can be omitted from being untracked when
                // the system does not support them.
                if (AbsolutePath.TryCreate(pathTable, SpecialFolderUtilities.GetFolderPath(specialFolder), out var root))
                {
                    if (subFolders != null)
                    {
                        foreach (var subFolder in subFolders)
                        {
                            root = root.Combine(pathTable, subFolder);
                        }
                    }

                    return DirectoryArtifact.CreateWithZeroPartialSealId(root);
                }

                return DirectoryArtifact.Invalid;
            }
        }
    }
}
