// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Pips
{
    /// <summary>
    /// Represents a runtime representation of a sealed directory.
    /// </summary>
    public sealed class StaticDirectory : IImplicitPath
    {
        /// <summary>
        ///  The root of the Static Directory
        /// </summary>
        public DirectoryArtifact Root { get; }

        /// <summary>
        /// The contents
        /// </summary>
        public SortedReadOnlyArray<FileArtifact, OrdinalPathOnlyFileArtifactComparer> Contents { get; }

        /// <nodoc/>
        public SealDirectoryKind SealDirectoryKind { get; }

        /// <summary>
        /// The DScript string literal version of <see cref="SealDirectoryKind"/>
        /// </summary>
        /// <remarks>
        /// This member is exposed via the DScript prelude
        /// </remarks>
        public string Kind { get; }

        /// <summary>
        /// Invalid artifact for uninitialized fields.
        /// </summary>
        public static readonly StaticDirectory Invalid = default(StaticDirectory);

        /// <summary>
        /// Creates the representation of an already-sealed directory.
        /// </summary>
        public StaticDirectory(DirectoryArtifact root, SealDirectoryKind kind, SortedReadOnlyArray<FileArtifact, OrdinalPathOnlyFileArtifactComparer> contents)
        {
            Contract.Requires(root.IsValid);
            // Opaques and shared opaques should always have empty content
            Contract.Requires(!kind.IsDynamicKind() || contents.Length == 0);

            Root = root;
            Contents = contents;
            SealDirectoryKind = kind;
            Kind = GetScriptRepresentation(SealDirectoryKind);
        }

        /// <summary>
        /// Keep in sync with StaticDirectoryKind in Prelude.IO
        /// </summary>
        private string GetScriptRepresentation(SealDirectoryKind sealDirectoryKind)
        {
            switch (sealDirectoryKind)
            {
                case SealDirectoryKind.Full:
                    return "full";
                case SealDirectoryKind.Partial:
                    return "partial";
                case SealDirectoryKind.SourceAllDirectories:
                    return "sourceAllDirectories";
                case SealDirectoryKind.SourceTopDirectoryOnly:
                    return "sourceTopDirectories";
                case SealDirectoryKind.Opaque:
                    return "exclusive";
                case SealDirectoryKind.SharedOpaque:
                    return "shared";
                default:
                    throw new InvalidOperationException(I($"Unexpected value '{sealDirectoryKind}'."));
            }
        }

        /// <summary>
        /// Attempts to find a file artifact corresponding to the given path that was declared as a member of this directory.
        /// </summary>
        public bool TryGetFileArtifact(AbsolutePath path, out FileArtifact artifact)
        {
            Contract.Requires(path.IsValid);
            Contract.Ensures(Contract.ValueAtReturn(out artifact).IsValid == Contract.Result<bool>());

            FileArtifact search = FileArtifact.CreateSourceFile(path);
            int maybeIndex = Contents.BinarySearch(search, 0, Contents.Length);
            if (maybeIndex < 0)
            {
                artifact = FileArtifact.Invalid;
                return false;
            }

            artifact = Contents[maybeIndex];
            return true;
        }

        /// <inheritdoc />
        public AbsolutePath Path => Root.Path;
    }
}
