// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Native.IO;
using BuildXL.Processes;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;

namespace Test.BuildXL.Processes.Detours
{
    /// <summary>
    /// Class for validating file access manifest.
    /// </summary>
    internal class ValidationDataCreator
    {
        private readonly FileAccessManifest m_manifest;
        private readonly PathTable m_pathTable;

        public List<ValidationData> DataItems { get; private set; }

        /// <summary>
        /// Creates an instance of <see cref="ValidationDataCreator"/>.
        /// </summary>
        public ValidationDataCreator(FileAccessManifest manifest, PathTable pathTable)
        {
            m_manifest = manifest;
            m_pathTable = pathTable;
            DataItems = new List<ValidationData>();
        }

        /// <summary>
        /// Adds scope policy.
        /// </summary>
        public AbsolutePath AddScope(
            string path,
            FileAccessPolicy values,
            FileAccessPolicy mask = FileAccessPolicy.Deny,
            FileAccessPolicy basePolicy = FileAccessPolicy.Deny)
        {
            AbsolutePath scopeAbsolutePath = AbsolutePath.Create(m_pathTable, path);
            var dataItem =
                new ValidationData
                {
                    Path = path,
                    PathId = scopeAbsolutePath.Value.Value,
                    NodePolicy = (basePolicy & mask) | values,
                    ConePolicy = null,
                    ExpectedUsn = ReportedFileAccess.NoUsn
                };

            DataItems.Add(dataItem);
            m_manifest.AddScope(scopeAbsolutePath, mask, values);

            return scopeAbsolutePath;
        }

        /// <summary>
        /// Adds path policy.
        /// </summary>
        public void AddPath(
            string path,
            FileAccessPolicy policy,
            FileAccessPolicy? expectedEffectivePolicy = null,
            Usn? expectedUsn = null)
        {
            AbsolutePath absolutePath = AbsolutePath.Create(m_pathTable, path);
            var dataItem =
                new ValidationData
                {
                    Path = path,
                    PathId = absolutePath.Value.Value,
                    ConePolicy = null,
                    NodePolicy = expectedEffectivePolicy ?? policy,
                    ExpectedUsn = expectedUsn ?? ReportedFileAccess.NoUsn
                };

            DataItems.Add(dataItem);
            m_manifest.AddPath(absolutePath, values: policy, mask: FileAccessPolicy.MaskNothing, expectedUsn: expectedUsn);
        }

        /// <summary>
        /// Adds check for scope policy without adding the policy to the file access manifest.
        /// </summary>
        public void AddScopeCheck(string path, AbsolutePath scopePath, FileAccessPolicy policy)
        {
            DataItems.Add(
                new ValidationData
                {
                    Path = path,
                    PathId = scopePath.Value.Value,
                    ConePolicy = policy,
                    NodePolicy = null,
                    ExpectedUsn = ReportedFileAccess.NoUsn
                });
        }

        /// <summary>
        /// Tests file access manifest.
        /// </summary>
        public static void TestManifestRetrieval(IEnumerable<ValidationData> validationData, FileAccessManifest fam, bool serializeManifest)
        {
            foreach (var line in fam.Describe())
            {
                Console.WriteLine(line);
            }

            if (serializeManifest)
            {
                var writtenFlag = fam.Flag;
                var writtenDirectoryTranslator = fam.DirectoryTranslator;

                string file = Path.GetTempFileName();

                using (FileStream fileStream = File.OpenWrite(file))
                {
                    fam.Serialize(fileStream);
                }

                using (FileStream fileStream = File.OpenRead(file))
                {
                    fam = FileAccessManifest.Deserialize(fileStream);
                }

                XAssert.AreEqual(writtenFlag, fam.Flag);
                XAssert.AreEqual(writtenDirectoryTranslator == null, fam.DirectoryTranslator == null);
                if (writtenDirectoryTranslator != null)
                {
                    XAssert.AreEqual(writtenDirectoryTranslator.Count, fam.DirectoryTranslator.Count);
                    var writtenTranslations = writtenDirectoryTranslator.Translations.ToArray();
                    var readTranslations = fam.DirectoryTranslator.Translations.ToArray();

                    for (int i = 0; i < writtenTranslations.Length; ++i)
                    {
                        XAssert.AreEqual(writtenTranslations[i].SourcePath, readTranslations[i].SourcePath);
                        XAssert.AreEqual(writtenTranslations[i].TargetPath, readTranslations[i].TargetPath);
                    }
                }
            }

            byte[] manifestTreeBytes = fam.GetManifestTreeBytes();

            foreach (ValidationData dataItem in validationData)
            {
                uint nodePolicy;
                uint conePolicy;
                uint pathId;
                Usn expectedUsn;

                bool success =
                    global::BuildXL.Native.Processes.Windows.ProcessUtilitiesWin.FindFileAccessPolicyInTree(
                        manifestTreeBytes,
                        dataItem.Path,
                        new UIntPtr((uint)dataItem.Path.Length),
                        out conePolicy,
                        out nodePolicy,
                        out pathId,
                        out expectedUsn);

                XAssert.IsTrue(success, "Unable to find path in manifest");
                XAssert.AreEqual(
                    unchecked((uint)dataItem.PathId),
                    pathId,
                    "PathId for '{0}' did not match", dataItem.Path);

                if (dataItem.NodePolicy.HasValue)
                {
                    XAssert.AreEqual(
                        unchecked((uint)dataItem.NodePolicy.Value),
                        nodePolicy,
                        "Policy for '{0}' did not match", dataItem.Path);
                }

                if (dataItem.ConePolicy.HasValue)
                {
                    XAssert.AreEqual(
                        unchecked((uint)dataItem.ConePolicy.Value),
                        conePolicy,
                        "Policy for '{0}' did not match", dataItem.Path);
                }

                XAssert.AreEqual(
                    dataItem.ExpectedUsn,
                    expectedUsn,
                    "Usn for '{0}' did not match", dataItem.Path);
            }
        }

        internal struct ValidationData
        {
            public string Path { get; set; }

            public FileAccessPolicy? NodePolicy { get; set; }

            public FileAccessPolicy? ConePolicy { get; set; }

            public int PathId { get; set; }

            public Usn ExpectedUsn { get; set; }
        }
    }
}
