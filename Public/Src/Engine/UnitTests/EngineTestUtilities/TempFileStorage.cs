// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading;
using BuildXL.Native.IO;
using BuildXL.Processes;
using BuildXL.Utilities;

namespace Test.BuildXL.TestUtilities
{
    /// <summary>
    /// Test helper class that produces unique file names, and deletes those files when disposed
    /// </summary>
    public sealed class TempFileStorage : ISandboxedProcessFileStorage, IDisposable
    {
        private readonly bool m_canGetFileNames;
        private readonly List<string> m_fileNames = new List<string>();
        private readonly HashSet<string> m_directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<SandboxedProcessFile, string> m_sandboxedProcessFiles = new Dictionary<SandboxedProcessFile, string>();
        private volatile string m_rootDirectory;

        /// <summary>
        /// Constructor
        /// </summary>
        public TempFileStorage(bool canGetFileNames, string rootPath = null)
        {
            m_canGetFileNames = canGetFileNames;
            m_rootDirectory = rootPath;
        }

        private static void TryDeleteFile(string fileName)
        {
            try
            {
                if (File.Exists(fileName))
                {
                    File.SetAttributes(fileName, FileAttributes.Normal);
                    File.Delete(fileName);
                }
            }
            catch (IOException)
            {
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            foreach (string fileName in m_fileNames)
            {
                TryDeleteFile(fileName);
            }

            foreach (string directory in m_directories)
            {
                try
                {
                    if (System.IO.Directory.Exists(directory))
                    {
                        FileUtilities.DeleteDirectoryContents(directory, deleteRootDirectory: true);
                    }
                }
                catch (IOException)
                {
                }
            }
        }

        string ISandboxedProcessFileStorage.GetFileName(SandboxedProcessFile file)
        {
            string fileName;
            if (!m_sandboxedProcessFiles.TryGetValue(file, out fileName))
            {
                m_sandboxedProcessFiles.Add(file, fileName = GetUniqueFileName());
            }

            return fileName;
        }

        /// <summary>
        /// Gets a unique file name as a full path.
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1026:DefaultParametersShouldNotBeUsed")]
        public string GetUniqueFileName(string prefix = null, string suffix = null)
        {
            // avoid .tmp extension, as detours services have special handling for them.
            return GetFileName(RootDirectory, (prefix ?? string.Empty) + Guid.NewGuid() + (suffix ?? ".txt"));
        }

        /// <summary>
        /// Gets a unique file name as a full path.
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1026:DefaultParametersShouldNotBeUsed")]
        public AbsolutePath GetUniqueFileName(PathTable pathTable, string prefix = null, string suffix = null)
        {
            Contract.Requires(pathTable != null);
            return AbsolutePath.Create(pathTable, GetUniqueFileName(prefix, suffix));
        }

        /// <summary>
        /// Gets a unique directory name as a full path.
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1026:DefaultParametersShouldNotBeUsed")]
        public string GetUniqueDirectory(string prefix = null)
        {
            return GetDirectory((prefix ?? string.Empty) + Guid.NewGuid());
        }

        /// <summary>
        /// Gets a unique directory name.
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1026:DefaultParametersShouldNotBeUsed")]
        public AbsolutePath GetUniqueDirectory(PathTable pathTable, string prefix = null)
        {
            Contract.Requires(pathTable != null);
            return AbsolutePath.Create(pathTable, GetUniqueDirectory(prefix));
        }

        private static int s_unique;

        /// <summary>
        /// Gets the name of the directory unique for this instance
        /// </summary>
        public string RootDirectory
        {
            get
            {
                if (m_rootDirectory == null)
                {
                    lock (this)
                    {
                        if (m_rootDirectory == null)
                        {
                            m_rootDirectory = Path.Combine(Environment.GetEnvironmentVariable("TEMP"), "temp" + Interlocked.Increment(ref s_unique));

                            if (System.IO.Directory.Exists(m_rootDirectory))
                            {
                                FileUtilities.DeleteDirectoryContents(m_rootDirectory);
                            }
                            else
                            {
                                System.IO.Directory.CreateDirectory(m_rootDirectory);
                            }
                        }
                    }
                }

                return m_rootDirectory;
            }
        }

        /// <summary>
        /// Gets a file name in a (per instance) directory as a full path.
        /// </summary>
        public string GetFileName(string fileName)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(fileName));
            return GetFileName(RootDirectory, fileName);
        }

        /// <summary>
        /// Gets a file name in a (per instance) directory as a full path.
        /// </summary>
        public AbsolutePath GetFileName(PathTable pathTable, string fileName)
        {
            Contract.Requires(pathTable != null);
            Contract.Requires(!string.IsNullOrWhiteSpace(fileName));

            return AbsolutePath.Create(pathTable, GetFileName(fileName));
        }

        /// <summary>
        /// Gets a file name as a full path within a directory that is obtained from this file storage.
        /// </summary>
        public string GetFileName(string directoryPath, string fileName)
        {
            Contract.Requires(ExistsDirectory(directoryPath));
            Contract.Requires(!string.IsNullOrWhiteSpace(fileName));

            AssertCanGetFileNames();
            string filePath = Path.Combine(directoryPath, fileName);
            m_fileNames.Add(filePath);

            return filePath;
        }

        /// <summary>
        /// Gets a file name as a full path within a directory that is obtained from this file storage.
        /// </summary>
        public AbsolutePath GetFileName(PathTable pathTable, AbsolutePath directoryPath, string fileName)
        {
            Contract.Requires(pathTable != null);
            Contract.Requires(directoryPath.IsValid);
            Contract.Requires(!string.IsNullOrWhiteSpace(fileName));

            return AbsolutePath.Create(pathTable, GetFileName(directoryPath.ToString(pathTable), fileName));
        }

        /// <summary>
        /// Gets a directory as a full path given a directory name.
        /// </summary>
        public string GetDirectory(string directoryName)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(directoryName));
            return GetDirectory(RootDirectory, directoryName);
        }

        /// <summary>
        /// Gets a directory as a full path given a directory name.
        /// </summary>
        public string GetDirectory(string directoryPath, string directoryName)
        {
            Contract.Requires(ExistsDirectory(directoryPath));
            Contract.Requires(!string.IsNullOrWhiteSpace(directoryName));

            AssertCanGetFileNames();
            string directory = Path.Combine(directoryPath, directoryName);

            if (ExistsDirectory(directory))
            {
                return directory;
            }

            if (Directory.Exists(directory))
            {
                FileUtilities.DeleteDirectoryContents(directory);
            }
            else
            {
                Directory.CreateDirectory(directory);
            }

            m_directories.Add(directory);
            return directory;
        }

        /// <summary>
        /// Gets a directory as a full path given a directory name.
        /// </summary>
        public AbsolutePath GetDirectory(PathTable pathTable, string directoryName)
        {
            Contract.Requires(pathTable != null);
            Contract.Requires(!string.IsNullOrWhiteSpace(directoryName));

            return AbsolutePath.Create(pathTable, GetDirectory(directoryName));
        }

        /// <summary>
        /// Gets a directory as a full path given a directory name.
        /// </summary>
        public AbsolutePath GetDirectory(PathTable pathTable, AbsolutePath directoryPath, string directoryName)
        {
            Contract.Requires(pathTable != null);
            Contract.Requires(directoryPath.IsValid);
            Contract.Requires(!string.IsNullOrWhiteSpace(directoryName));

            return AbsolutePath.Create(pathTable, GetDirectory(directoryPath.ToString(pathTable), directoryName));
        }

        /// <summary>
        /// Checks if a directory exists in this file storage.
        /// </summary>
        [Pure]
        public bool ExistsDirectory(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                return false;
            }

            return m_directories.Contains(directoryPath) || string.Equals(RootDirectory, directoryPath, StringComparison.OrdinalIgnoreCase);
        }

        private void AssertCanGetFileNames()
        {
            if (!m_canGetFileNames)
            {
                throw new BuildXLTestException("Getting file names was not enabled by this test's constructor");
            }
        }
    }
}
