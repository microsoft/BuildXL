// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Pips.Builders
{
    /// <nodoc />
    public static class ProcessBuilderExtensionsForTesting
    {
        /// <nodoc />
        public static void AddInputFile(this ProcessBuilder builder, AbsolutePath file)
        {
            builder.AddInputFile(FileArtifact.CreateSourceFile(file));
        }

        /// <nodoc />
        public static void AddInputFile(this ProcessBuilder builder, PathTable pathTable, string file)
        {
            builder.AddInputFile(AbsolutePath.Create(pathTable, file));
        }

        /// <nodoc />
        public static void AddOutputFile(this ProcessBuilder builder, AbsolutePath file)
        {
            builder.AddOutputFile(file, FileExistence.Required);
        }

        /// <nodoc />
        public static void AddOutputFile(this ProcessBuilder builder, FileArtifact file)
        {
            builder.AddOutputFile(file, FileExistence.Required);
        }

        /// <nodoc />
        public static void AddOutputFile(this ProcessBuilder builder, PathTable pathTable, string file, FileExistence fileExistence = FileExistence.Required)
        {
            builder.AddOutputFile(AbsolutePath.Create(pathTable, file), fileExistence);
        }

        /// <nodoc />
        public static void AddOutputDirectory(this ProcessBuilder builder, PathTable pathTable, string directory, SealDirectoryKind kind = SealDirectoryKind.Opaque)
        {
            builder.AddOutputDirectory(AbsolutePath.Create(pathTable, directory), kind);
        }

        /// <nodoc />
        public static void AddOutputDirectory(this ProcessBuilder builder, AbsolutePath directory, SealDirectoryKind kind = SealDirectoryKind.Opaque)
        {
            builder.AddOutputDirectory(DirectoryArtifact.CreateWithZeroPartialSealId(directory), kind);
        }

        /// <nodoc />
        public static void AddUntrackedDirectoryScope(this ProcessBuilder builder, AbsolutePath directory)
        {
            builder.AddUntrackedDirectoryScope(DirectoryArtifact.CreateWithZeroPartialSealId(directory));
        }

        /// <nodoc />
        public static void AddUntrackedDirectoryScope(this ProcessBuilder builder, PathTable pathTable, string directory)
        {
            builder.AddUntrackedDirectoryScope(DirectoryArtifact.CreateWithZeroPartialSealId(AbsolutePath.Create(pathTable, directory)));
        }

        /// <nodoc />
        public static void AddTags(this ProcessBuilder processBuilder, StringTable stringTable, params string[] tags)
        {
            var tagIds = new StringId[tags.Length];
            for (int i = 0; i < tags.Length; i++)
            {
                tagIds[i] = StringId.Create(stringTable, tags[i]);
            }

            processBuilder.Tags = ReadOnlyArray<StringId>.FromWithoutCopy(tagIds);
        }

        /// <nodoc />
        public static FileArtifact GetOutputFile(this ProcessOutputs processOutputs, AbsolutePath path)
        {
            if (!processOutputs.TryGetOutputFile(FileArtifact.CreateSourceFile(path), out var file))
            {
                throw new InvalidOperationException();
            }

            return file;
        }

        /// <nodoc />
        public static DirectoryArtifact GetOpaqueDirectory(this ProcessOutputs processOutputs, AbsolutePath path)
        {
            if (!processOutputs.TryGetOutputDirectory(path, out var result))
            {
                throw new InvalidOperationException();
            }

            return result.Root;
        }

        /// <nodoc />
        public static DirectoryArtifact SealDirectoryFull(
            this PipConstructionHelper pipConstructionHelper,
            AbsolutePath directoryRoot,
            FileArtifact[] contents,
            string[] tags = null,
            string description = null)
        {
            return SealDirectoryPartialOrFull(
                pipConstructionHelper,
                directoryRoot,
                SealDirectoryKind.Full,
                contents,
                tags,
                description,
                patterns: null);
        }

        /// <nodoc />
        public static DirectoryArtifact SealDirectoryPartial(
            this PipConstructionHelper pipConstructionHelper,
            AbsolutePath directoryRoot,
            FileArtifact[] contents,
            string[] tags = null,
            string description = null)
        {
            return SealDirectoryPartialOrFull(
                pipConstructionHelper,
                directoryRoot,
                SealDirectoryKind.Partial,
                contents,
                tags,
                description,
                patterns: null);
        }

        /// <nodoc />
        private static DirectoryArtifact SealDirectoryPartialOrFull(
            PipConstructionHelper pipConstructionHelper,
            AbsolutePath directoryRoot,
            SealDirectoryKind kind,
            FileArtifact[] contents,
            string[] tags = null,
            string description = null,
            string[] patterns = null)
        {
            if (!pipConstructionHelper.TrySealDirectory(
                directoryRoot,
                SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>.CloneAndSort(
                    contents,
                    OrdinalFileArtifactComparer.Instance),
                kind,
                tags,
                description,
                patterns,
                out var sealedDirectory
            ))
            {
                throw new InvalidOperationException();
            }

            return sealedDirectory;
        }

        /// <nodoc />
        public static DirectoryArtifact SealDirectorySource(
            this PipConstructionHelper pipConstructionHelper,
            AbsolutePath directoryRoot,
            SealDirectoryKind kind = SealDirectoryKind.SourceAllDirectories,
            string[] tags = null,
            string description = null,
            string[] patterns = null)
        {
            if (!pipConstructionHelper.TrySealDirectory(
                directoryRoot,
                CollectionUtilities.EmptySortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>(OrdinalFileArtifactComparer.Instance),
                kind,
                tags,
                description,
                patterns,
                out var sealedDirectory
            ))
            {
                throw new InvalidOperationException();
            }

            return sealedDirectory;
        }

        /// <nodoc />
        public static ProcessOutputs AddProcess(
            this PipConstructionHelper pipConstructionHelper,
            ProcessBuilder builder)
        {
            if (!pipConstructionHelper.TryAddProcess(builder, out var outputs, out _))
            {
                throw new InvalidOperationException();
            }

            return outputs;
        }


        /// <nodoc />
        public static bool TryAddProcess(
            this PipConstructionHelper pipConstructionHelper,
            ProcessBuilder builder)
        {
            return pipConstructionHelper.TryAddProcess(builder, out _, out _);
        }
    }
}
