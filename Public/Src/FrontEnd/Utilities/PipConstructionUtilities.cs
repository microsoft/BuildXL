// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Resolvers;

namespace BuildXL.FrontEnd.Utilities
{
    /// <summary>
    /// Static methods with common logic for the FrontEnd resolvers,
    /// grouping methods associated with pip construction
    /// </summary>
    public class PipConstructionUtilities
    {
#pragma warning disable SYSLIB0021 // Type or member is obsolete. Temporarily suppressing the warning for .net 6. Work item: 1885580
        private static readonly SHA256 s_hashManager = SHA256Managed.Create();
#pragma warning restore SYSLIB0021 // Type or member is obsolete

        /// <summary>
        /// Returns a string that will be valid for constructing a symbol by replacing all invalid characters
        /// with a valid one
        /// </summary>
        /// <remarks>
        /// Invalid characters are replaced by '$'. Invalid start characters are preserved if possible by shifting
        /// them to occur after a '$'.
        /// </remarks>
        public static string SanitizeStringForSymbol(string aString)
        {
            // The case of an empty value for a property is preserved as is
            // Other cases (project full path or property key) should never be empty
            if (String.IsNullOrEmpty(aString))
            {
                return String.Empty;
            }

            var builder = new StringBuilder(aString.Length);
            var allAtoms = aString.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            for (int j = 0; j < allAtoms.Length; j++)
            {
                var atom = allAtoms[j];
                char firstChar = atom[0];

                builder.Append(!SymbolAtom.IsValidIdentifierAtomStartChar(firstChar) ? '_' : firstChar);
                // If the character is not valid as a first character, but valid as a second one, we add it as well
                // This is useful for things like package.1.2, so we don't drop the version numbers completely
                if (!SymbolAtom.IsValidIdentifierAtomStartChar(firstChar) && SymbolAtom.IsValidIdentifierAtomChar(firstChar))
                {
                    builder.Append(firstChar);
                }

                for (int i = 1; i < atom.Length; i++)
                {
                    var aChar = atom[i];
                    builder.Append(!SymbolAtom.IsValidIdentifierAtomChar(aChar) ? '_' : aChar);
                }

                if (j != allAtoms.Length - 1)
                {
                    builder.Append('.');
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// Some FrontEnds allow configurable untracking of files, directories and directory scopes
        /// This method applies that configuration to the process builder
        /// </summary>
        public static void UntrackUserConfigurableArtifacts(PathTable pathTable, AbsolutePath currentProjectRoot, IEnumerable<AbsolutePath> allProjectRoots, ProcessBuilder processBuilder, IUntrackingSettings settings)
        {
            Contract.AssertNotNull(settings);
            Contract.Assert(currentProjectRoot.IsValid);
            Contract.AssertNotNull(allProjectRoots);
            Contract.AssertNotNull(processBuilder);
            Contract.AssertNotNull(settings);

            if (settings.UntrackedDirectoryScopes != null)
            {
                foreach (var untrackedDirectoryScopeUnion in settings.UntrackedDirectoryScopes)
                {
                    DirectoryArtifact untrackedDirectoryScope = ResolveAbsoluteOrRelativeDirectory(pathTable, untrackedDirectoryScopeUnion, currentProjectRoot);
                    if (!untrackedDirectoryScope.IsValid)
                    {
                        continue;
                    }
                    processBuilder.AddUntrackedDirectoryScope(untrackedDirectoryScope);
                }
            }

            if (settings.UntrackedDirectories != null)
            {
                foreach (var untrackedDirectoryUnion in settings.UntrackedDirectories)
                {
                    DirectoryArtifact untrackedDirectory = ResolveAbsoluteOrRelativeDirectory(pathTable, untrackedDirectoryUnion, currentProjectRoot);

                    if (!untrackedDirectory.IsValid)
                    {
                        continue;
                    }
                    processBuilder.AddUntrackedDirectoryScope(untrackedDirectory);
                }
            }

            if (settings.UntrackedFiles != null)
            {
                foreach (var untrackedFile in settings.UntrackedFiles)
                {
                    if (!untrackedFile.IsValid)
                    {
                        continue;
                    }
                    processBuilder.AddUntrackedFile(untrackedFile);
                }
            }

            if (settings.UntrackedGlobalDirectoryScopes != null)
            {
                foreach(var relativeDirectory in settings.UntrackedGlobalDirectoryScopes)
                {
                    if (!relativeDirectory.IsValid)
                    {
                        continue;
                    }

                    foreach(var projectRoot in allProjectRoots)
                    {
                        processBuilder.AddUntrackedDirectoryScope(DirectoryArtifact.CreateWithZeroPartialSealId(projectRoot.Combine(pathTable, relativeDirectory)));
                    }
                }
            }

            if (settings.ChildProcessesToBreakawayFromSandbox != null)
            {
                processBuilder.ChildProcessesToBreakawayFromSandbox = settings.ChildProcessesToBreakawayFromSandbox.Where(processName => processName.IsValid).ToReadOnlyArray();
            }
        }

        private static DirectoryArtifact ResolveAbsoluteOrRelativeDirectory(PathTable pathTable, DiscriminatingUnion<DirectoryArtifact, RelativePath> absoluteOrRelativeUnion, AbsolutePath root)
        {
            var absoluteOrRelative = absoluteOrRelativeUnion.GetValue();
            if (absoluteOrRelative is DirectoryArtifact directory)
            {
                return directory;
            }

            var relative = (RelativePath) absoluteOrRelative;

            if (!relative.IsValid)
            {
                return DirectoryArtifact.Invalid;
            }

            return DirectoryArtifact.CreateWithZeroPartialSealId(root.Combine(pathTable, relative));
        }

        /// <nodoc />
        public static string ComputeSha256(string content)
        {
            byte[] contentBytes = Encoding.UTF8.GetBytes(content);
            byte[] hashBytes = s_hashManager.ComputeHash(contentBytes);

            return hashBytes.ToHex();
        }
        
        /// <nodoc />
        public static void AddAdditionalOutputDirectories(ProcessBuilder processBuilder, IReadOnlyList<DiscriminatingUnion<AbsolutePath, RelativePath>> directories, AbsolutePath root, PathTable pathTable)
        {
            if (directories == null)
            {
                return;
            }

            foreach (DiscriminatingUnion<AbsolutePath, RelativePath> directoryUnion in directories)
            {
                object directory = directoryUnion.GetValue();
                if (directory is AbsolutePath absolutePath)
                {
                    Contract.Assert(absolutePath.IsValid);
                    processBuilder.AddOutputDirectory(DirectoryArtifact.CreateWithZeroPartialSealId(absolutePath), SealDirectoryKind.SharedOpaque);
                }
                else
                {
                    // The specified relative path is interpreted relative to the specified root
                    var relative = (RelativePath)directory;
                    Contract.Assert(relative.IsValid);
                    AbsolutePath absoluteDirectory = root.Combine(pathTable, relative);
                    processBuilder.AddOutputDirectory(DirectoryArtifact.CreateWithZeroPartialSealId(absoluteDirectory), SealDirectoryKind.SharedOpaque);
                }
            }
        }

    }
}
