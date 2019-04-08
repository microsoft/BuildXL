// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Security.Cryptography;
using System.Text;
using BuildXL.Pips.Builders;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration.Resolvers;

namespace BuildXL.FrontEnd.Utilities
{
    /// <summary>
    /// Static methods with common logic for the FrontEnd resolvers,
    /// grouping methods associated with pip construction
    /// </summary>
    public class PipConstructionUtilities
    {
        private static readonly SHA256 s_hashManager = SHA256Managed.Create();

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

                builder.Append(!SymbolAtom.IsValidIdentifierAtomStartChar(firstChar) ? '$' : firstChar);
                // If the character is not valid as a first character, but valid as a second one, we add it as well
                // This is useful for things like package.1.2, so we don't drop the version numbers completely
                if (!SymbolAtom.IsValidIdentifierAtomStartChar(firstChar) && SymbolAtom.IsValidIdentifierAtomChar(firstChar))
                {
                    builder.Append(firstChar);
                }

                for (int i = 1; i < atom.Length; i++)
                {
                    var aChar = atom[i];
                    builder.Append(!SymbolAtom.IsValidIdentifierAtomChar(aChar) ? '$' : aChar);
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
        public static void UntrackUserConfigurableArtifacts(ProcessBuilder processBuilder, IUntrackingSettings settings)
        {
            Contract.Assert(settings != null);
            if (settings.UntrackedDirectoryScopes != null)
            {
                foreach (var untrackedDirectoryScope in settings.UntrackedDirectoryScopes)
                {
                    if (!untrackedDirectoryScope.IsValid)
                    {
                        continue;
                    }
                    processBuilder.AddUntrackedDirectoryScope(untrackedDirectoryScope);
                }
            }

            if (settings.UntrackedDirectories != null)
            {
                foreach (var untrackedDirectory in settings.UntrackedDirectories)
                {
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
        }

        /// <nodoc />
        public static string ComputeSha256(string content)
        {
            byte[] contentBytes = Encoding.UTF8.GetBytes(content);
            byte[] hashBytes = s_hashManager.ComputeHash(contentBytes);

            return hashBytes.ToHex();
        }
    }
}
