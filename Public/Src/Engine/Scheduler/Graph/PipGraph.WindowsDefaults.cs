using System;
using System.IO;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;

namespace BuildXL.Scheduler.Graph
{
    partial class PipGraph
    {
        /// <nodoc />
        public class WindowsOsDefaults
        {
            private readonly DirectoryArtifact[] m_untrackedDirectories;
            
            private readonly DirectoryArtifact m_applicationDataPath;
            private readonly DirectoryArtifact m_localApplicationDataPath;

            private readonly DirectoryArtifact m_commonApplicationDataPath;

            /// <nodoc />
            public WindowsOsDefaults(PathTable pathTable)
            {
                m_untrackedDirectories =
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
            public void ProcessDefaults(ProcessBuilder processBuilder)
            {
                if ((processBuilder.Options & Process.Options.DependsOnCurrentOs) != 0)
                {
                    foreach (var untrackedDirectory in m_untrackedDirectories)
                    {
                        processBuilder.AddUntrackedDirectoryScope(untrackedDirectory);
                    }
                }

                if ((processBuilder.Options & Process.Options.DependsOnWindowsAppData) != 0)
                {
                    processBuilder.AddUntrackedDirectoryScope(m_applicationDataPath);
                    processBuilder.AddUntrackedDirectoryScope(m_localApplicationDataPath);
                }

                if ((processBuilder.Options & Process.Options.DependsOnWindowsProgramData) != 0)
                {
                    processBuilder.AddUntrackedDirectoryScope(m_commonApplicationDataPath);
                }
            }

            private static DirectoryArtifact GetSpecialFolder(PathTable pathTable, Environment.SpecialFolder specialFolder, params string[] subFolders)
            {
                var root = AbsolutePath.Create(pathTable, SpecialFolderUtilities.GetFolderPath(specialFolder));
                if (subFolders != null)
                {
                    foreach (var subFolder in subFolders)
                    {
                        root = root.Combine(pathTable, subFolder);
                    }
                }

                return DirectoryArtifact.CreateWithZeroPartialSealId(root);
            }
        }
    }
}
