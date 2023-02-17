// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// An exception rule for the directory membership fingerprinter
    /// </summary>
    public sealed class DirectoryMembershipFingerprinterRule
    {
        /// <summary>
        /// Wildcard comparer.
        /// </summary>
        public static StringComparer WildcardComparer => OperatingSystemHelper.PathComparer;

        /// <summary>
        /// Wildcard comparsion.
        /// </summary>
        public static StringComparison WildcardComparison => OperatingSystemHelper.PathComparison;

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

        /// <summary>
        /// Whether this rule is applied to all directories under <see cref="Root"/>.
        /// </summary>
        public bool Recursive { get; }

        /// <summary>
        /// Wildcards to ignore files.
        /// </summary>
        public IEnumerable<string> FileIgnoreWildcards => m_fileIgnoreWildcards;

        private readonly IReadOnlyList<string> m_fileIgnoreWildcards;


        /// <nodoc/>
        public DirectoryMembershipFingerprinterRule(
            string name,
            AbsolutePath root,
            bool disableFilesystemEnumeration,
            IReadOnlyList<string> fileIgnoreWildcards,
            bool recursive)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(name));
            Contract.Requires(root.IsValid);
            Contract.Requires(disableFilesystemEnumeration ^ (fileIgnoreWildcards != null && fileIgnoreWildcards.Count > 0));

            Name = name;
            Root = root;
            DisableFilesystemEnumeration = disableFilesystemEnumeration;
            m_fileIgnoreWildcards = fileIgnoreWildcards ?? new List<string>();
            Recursive = recursive;
        }

        /// <nodoc/>
        public static DirectoryMembershipFingerprinterRule CreateFromConfig(StringTable stringTable, IDirectoryMembershipFingerprinterRule rule)
        {
            return new DirectoryMembershipFingerprinterRule(
                rule.Name,
                rule.Root,
                rule.DisableFilesystemEnumeration,
                rule.FileIgnoreWildcards.Select(wildCard => wildCard.ToString(stringTable)).ToList(),
                rule.Recursive);
        }

        /// <nodoc/>
        public void Serialize(BuildXLWriter writer)
        {
            writer.Write(Name);
            writer.Write(Root);
            writer.Write(DisableFilesystemEnumeration);
            writer.WriteReadOnlyList(m_fileIgnoreWildcards, (w, e) => w.Write(e));
            writer.Write(Recursive);
        }

        /// <nodoc/>
        public static DirectoryMembershipFingerprinterRule Deserialize(BuildXLReader reader)
        {
            string name = reader.ReadString();
            AbsolutePath root = reader.ReadAbsolutePath();
            bool disableFilesystemEnumeration = reader.ReadBoolean();
            IReadOnlyList<string> wildcards = reader.ReadReadOnlyList(r => r.ReadString());
            bool recursive = reader.ReadBoolean();

            return new DirectoryMembershipFingerprinterRule(name, root, disableFilesystemEnumeration, wildcards, recursive);
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
                    if (fileName.IndexOf(wildcard, WildcardComparison) >= 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
