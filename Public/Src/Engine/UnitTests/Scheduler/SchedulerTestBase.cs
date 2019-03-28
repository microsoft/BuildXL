// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using BuildXL.Scheduler.Filter;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.TestUtilities;
using Xunit.Abstractions;

namespace Test.BuildXL.Scheduler
{
    public abstract class SchedulerTestBase : PipTestBase
    {
        private const string SourceFilePrefix = "src_file_";
        private const string OutputFilePrefix = "out_file_";
        private const string SpecFilePrefix = "spec_";
        private const string FilePrefix = "file_";
        private const string DirectoryPrefix = "directory_";

        private Paths m_paths;

        private AbsolutePath m_readOnlyObjectRootPath = AbsolutePath.Invalid;
        private AbsolutePath m_nonHashableObjectRootPath = AbsolutePath.Invalid;
        private AbsolutePath m_nonReadableObjectRootPath = AbsolutePath.Invalid;

        private int m_fileFreshId;
        private int m_directoryFreshId;

        public SchedulerTestBase(ITestOutputHelper output)
            : base(output)
        {
            m_paths = new Paths(Context.PathTable);
            m_fileFreshId = 0;
        }

        protected AbsolutePath ReadOnlyObjectRootPath => m_readOnlyObjectRootPath;

        protected AbsolutePath NonHashableObjectRootPath => m_nonHashableObjectRootPath;

        protected AbsolutePath NonReadableObjectRootPath => m_nonReadableObjectRootPath;

        protected virtual void UpdateConfiguration(CommandLineConfiguration config)
        {
        }

        private bool TryCreateDirectoryPath(string directoryPath, out AbsolutePath path)
        {
            Contract.Requires(!string.IsNullOrEmpty(directoryPath));
            Contract.Ensures(!Contract.Result<bool>() || Contract.ValueAtReturn(out path).IsValid);

            path = AbsolutePath.Invalid;
            bool result = false;

            try
            {
                Directory.CreateDirectory(directoryPath);
                result = m_paths.TryCreateAbsolutePath(directoryPath, out path);
            }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
            catch
            {
            }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler

            return result;
        }

        /// <summary>
        /// Creates source file artifact.
        /// </summary>
        protected FileArtifact CreateSourceFile(string fileName = null, string content = null)
        {
            return CreateSourceFile(SourceRootPath, fileName, content);
        }

        /// <summary>
        /// Creates source file artifact.
        /// </summary>
        protected FileArtifact CreateSourceFile(AbsolutePath rootPath, string fileName = null, string content = null)
        {
            Contract.Requires(rootPath.IsValid);

            if (string.IsNullOrEmpty(fileName))
            {
                fileName = CreateUniqueFileName(SourceFilePrefix);
            }

            AbsolutePath filePath = m_paths.CreateAbsolutePath(rootPath, fileName);
            FileArtifact sourceFile = FileArtifact.CreateSourceFile(filePath);
            WriteFile(sourceFile, content: content, appendIfExists: false);

            return sourceFile;
        }

        /// <summary>
        /// Creates output/derived file artifact.
        /// </summary>
        protected FileArtifact CreateOutputFile(string fileName = null, int rewriteCount = -1)
        {
            return CreateOutputFile(ObjectRootPath, fileName, rewriteCount);
        }

        /// <summary>
        /// Creates output/derived file artifact.
        /// </summary>
        protected FileArtifact CreateOutputFile(AbsolutePath rootPath, string fileName = null, int rewriteCount = -1)
        {
            Contract.Requires(rootPath.IsValid);

            if (string.IsNullOrEmpty(fileName))
            {
                fileName = CreateUniqueFileName(OutputFilePrefix);
            }

            AbsolutePath filePath = m_paths.CreateAbsolutePath(rootPath, fileName);

            return rewriteCount < 1
                ? FileArtifact.CreateSourceFile(filePath).CreateNextWrittenVersion()
                : new FileArtifact(filePath, rewriteCount: rewriteCount);
        }

        /// <summary>
        /// Creates a directory artifact with zero partial seal id.
        /// </summary>
        protected DirectoryArtifact CreateDirectory(
            AbsolutePath rootPath = default(AbsolutePath),
            RelativePath relativePathToDirectory = default(RelativePath))
        {
            var directoryPath = CreateDirectoryPath(
                rootPath.IsValid ? rootPath : ObjectRootPath,
                relativePathToDirectory.IsValid
                    ? relativePathToDirectory
                    : RelativePath.Create(Context.StringTable, CreateUniqueDirectoryName()));

            return DirectoryArtifact.CreateWithZeroPartialSealId(directoryPath);
        }

        /// <summary>
        /// Creates an output directory.
        /// </summary>
        protected ValueTuple<DirectoryArtifact, ReadOnlyArray<FileArtifact>>  CreateOutputDirectory(
            AbsolutePath rootPath = default(AbsolutePath),
            RelativePath relativePathToDirectory = default(RelativePath),
            RelativePath[] relativePathToMembers = null)
        {
            var directory = CreateDirectory(rootPath, relativePathToDirectory);
            ReadOnlyArray<FileArtifact> members;

            if (relativePathToMembers == null || relativePathToMembers.Length == 0)
            {
                members = ReadOnlyArray<FileArtifact>.Empty;
            }
            else
            {
                var fullMemberNames = new FileArtifact[relativePathToMembers.Length];
                for (int i = 0; i < fullMemberNames.Length; ++i)
                {
                    fullMemberNames[i] = FileArtifact.CreateOutputFile(directory.Path.Combine(Context.PathTable, relativePathToMembers[i]));
                }

                members = ReadOnlyArray<FileArtifact>.FromWithoutCopy(fullMemberNames);
            }

            return (directory, members);
        }

        /// <summary>
        /// Creates a directory path relative to a root path.
        /// </summary>
        protected AbsolutePath CreateDirectoryPath(AbsolutePath rootPath, RelativePath relative)
        {
            Contract.Requires(rootPath.IsValid);
            Contract.Requires(relative.IsValid);

            return m_paths.CreateAbsolutePath(rootPath, relative);
        }

        private string CreateUniqueFileName(string filePrefix = null)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}{1}", filePrefix ?? FilePrefix, m_fileFreshId++);
        }

        private string CreateUniqueDirectoryName(string directoryPrefix = null)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}{1}", directoryPrefix ?? DirectoryPrefix, m_directoryFreshId++);
        }

        /// <summary>
        /// Writes to file artifact.
        /// </summary>
        protected void WriteFile(FileArtifact file, string content = null, bool appendIfExists = false)
        {
            content = content ?? Guid.NewGuid().ToString();
            string fullPath = m_paths.Expand(file.Path);
            string directoryPath = Path.GetDirectoryName(fullPath);

            if (!string.IsNullOrEmpty(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            if (appendIfExists && File.Exists(fullPath))
            {
                content += File.ReadAllText(fullPath);
            }

            File.WriteAllText(fullPath, content);
        }

        /// <summary>
        /// Deletes file artifact.
        /// </summary>
        protected void DeleteFile(FileArtifact file)
        {
            string fullPath = m_paths.Expand(file.Path);

            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }

        /// <summary>
        /// Creates a file content table.
        /// </summary>
        protected FileContentTable CreateFileContentTable()
        {
            Contract.Ensures(Contract.Result<FileContentTable>() != null);

            return FileContentTable.CreateNew();
        }

        /// <summary>
        /// Gets content hash.
        /// </summary>
        protected byte[] GetContentHash(FileArtifact file)
        {
            Contract.Requires(file.IsValid);

            byte[] hash = null;
            string fullPath = m_paths.Expand(file.Path);

            if (File.Exists(fullPath))
            {
                var stream = new MemoryStream(File.ReadAllBytes(fullPath));

                var contentHash = ContentHashingUtilities.HashContentStream(stream);
                return contentHash.ToHashByteArray();
            }

            return hash;
        }

        /// <summary>
        /// Reads file.
        /// </summary>
        protected string ReadFile(FileArtifact file)
        {
            Contract.Requires(file.IsValid);

            string fullPath = m_paths.Expand(file.Path);

            if (File.Exists(fullPath))
            {
                string s = File.ReadAllText(fullPath);
                return s;
            }

            return null;
        }

        /// <summary>
        /// Creates filter from tags.
        /// </summary>
        protected RootFilter CreateFilterFromTags(string tag1, string tag2 = null)
        {
            PipFilter filter;
            if (tag2 != null)
            {
                filter = new BinaryFilter(new TagFilter(StringId.Create(Context.PathTable.StringTable, tag1)),
                    FilterOperator.Or,
                    new TagFilter(StringId.Create(Context.PathTable.StringTable, tag2)));
            }
            else
            {
                filter = new TagFilter(StringId.Create(Context.PathTable.StringTable, tag1));
            }

            return new RootFilter(filter);
        }
    }
}
