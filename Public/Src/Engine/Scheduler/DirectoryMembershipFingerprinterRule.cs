// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// An exception rule for the directory membership fingerprinter
    /// </summary>
    public sealed class DirectoryMembershipFingerprinterRule
    {
        /// <summary>
        /// Name of the exception
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Path the exception applies to
        /// </summary>
        public AbsolutePath Root { get; }

        /// <summary>
        /// Whether to disable filesystem enumeration and force graph based enumeration
        /// </summary>
        public bool DisableFilesystemEnumeration { get; }

        private readonly IReadOnlyList<string> m_fileIgnoreWildcards;

        /// <nodoc/>
        public DirectoryMembershipFingerprinterRule(
            string name,
            AbsolutePath root,
            bool disableFilesystemEnumeration,
            IReadOnlyList<string> fileIgnoreWildcards)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(name));
            Contract.Requires(root.IsValid);
            Contract.Requires(disableFilesystemEnumeration ^ (fileIgnoreWildcards != null && fileIgnoreWildcards.Count > 0));

            Name = name;
            Root = root;
            DisableFilesystemEnumeration = disableFilesystemEnumeration;

            if (fileIgnoreWildcards == null)
            {
                m_fileIgnoreWildcards = new List<string>();
            }

            m_fileIgnoreWildcards = fileIgnoreWildcards;
        }

        /// <nodoc/>
        public static DirectoryMembershipFingerprinterRule CreateFromConfig(StringTable stringTable, IDirectoryMembershipFingerprinterRule rule)
        {
            return new DirectoryMembershipFingerprinterRule(
                rule.Name,
                rule.Root,
                rule.DisableFilesystemEnumeration,
                rule.FileIgnoreWildcards.Select(wildCard => wildCard.ToString(stringTable)).ToList());
        }

        /// <nodoc/>
        public void Serialize(BuildXLWriter writer)
        {
            writer.Write(Name);
            writer.Write(Root);
            writer.Write(DisableFilesystemEnumeration);
            writer.WriteCompact(m_fileIgnoreWildcards.Count);

            foreach (var wildcard in m_fileIgnoreWildcards)
            {
                writer.Write(wildcard);
            }
        }

        /// <nodoc/>
        public static DirectoryMembershipFingerprinterRule Deserialize(BuildXLReader reader)
        {
            string name = reader.ReadString();
            AbsolutePath root = reader.ReadAbsolutePath();
            bool disableFilesystemEnumeration = reader.ReadBoolean();
            int count = reader.ReadInt32Compact();
            List<string> wildcards = new List<string>(count);
            for (int i = 0; i < count; i++)
            {
                wildcards.Add(reader.ReadString());
            }

            return new DirectoryMembershipFingerprinterRule(name, root, disableFilesystemEnumeration, wildcards);
        }

        /// <summary>
        /// Checks to see if a file should be ignored when enumerating a directory.
        /// </summary>
        /// <param name="fileName">name of file being tested</param>
        /// <returns>true if the file should be ignored</returns>
        public bool ShouldIgnoreFileWhenEnumerating(string fileName)
        {
            if (!DisableFilesystemEnumeration)
            {
                foreach (string wildcard in m_fileIgnoreWildcards)
                {
                    if (fileName.IndexOf(wildcard, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
