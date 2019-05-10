// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text;
using BuildXL.Utilities;
using BuildXL.Utilities.Tasks;
using TypeScript.Net.Extensions;

namespace BuildXL.FrontEnd.Core
{
    /// <summary>
    /// Represents a content of a package's hash file.
    /// </summary>
    /// <remarks>
    /// This file is used for speed up-to-date check that a nuget package already layed out on disk can be reused without touching the cache or nuget.
    /// </remarks>
    internal readonly struct PackageHashFile
    {
        // The hash file version is separated to cover the following cases:
        //  * the file format itself, and
        //  * the format of the generated specs.
        // If the file format changes, we have to ignore the files on disk,
        // buf if the file format is the same and only generated specs format has changed,
        // then we can reuse files from disk and force specs regeneration.

        // The file format change will force specs regeneration.
        // Change the version if the nuget spec generation has changed in a backward incompatible way.
        private const string HashFileFormatVersion = "8";
        private const string GeneratedSpecsVersion = "14";

        /// <summary>
        /// The minimal number of lines for the hash file.
        /// </summary>
        private const int MinNumberOfLines = 4;

        /// <summary>
        /// Fingerprint hash of a nuget package.
        /// </summary>
        public string FingerprintHash { get; }

        /// <summary>
        /// Full fingerprint text of a nuget package.
        /// </summary>
        public string FingerprintText { get; }

        /// <summary>
        /// Package content. List of relative paths.
        /// </summary>
        public IReadOnlyList<string> Content { get; }

        /// <summary>
        /// Returns true if the spec format is still up-to-date.
        /// </summary>
        public bool SpecsFormatIsUpToDate { get; }

        /// <nodoc/>
        public PackageHashFile(string fingerprintHash, string fingerprintText, IReadOnlyList<string> content, bool specsFormatIsUpToDate)
        {
            Contract.Requires(!string.IsNullOrEmpty(fingerprintHash));
            Contract.Requires(!string.IsNullOrEmpty(fingerprintText));
            // Empty content means that something went wrong. The caller should make sure this never happen.
            Contract.Requires(!content.IsNullOrEmpty(), "Hash file with an empty content will lead to a break on the following invocation.");

            FingerprintHash = fingerprintHash;
            FingerprintText = fingerprintText;
            Content = new List<string>(content.OrderBy(id => id));
            SpecsFormatIsUpToDate = specsFormatIsUpToDate;
        }

        /// <summary>
        /// Tries to read a package's hash file from disk.
        /// </summary>
        /// <remarks>
        /// The <see cref="Failure"/> returned from this method is recoverable.
        /// </remarks>
        public static Possible<PackageHashFile> TryReadFrom(string path)
        {
            string[] content;
            try
            {
                content = ExceptionUtilities.HandleRecoverableIOException(
                    () =>
                    {
                        if (!File.Exists(path))
                        {
                            return null;
                        }

                        return File.ReadAllLines(path);
                    },
                    e => throw new BuildXLException(FormattableStringEx.I($"Failed to parse package hash file."), e));
            }
            catch (BuildXLException e)
            {
                return new PackageHashFileFailure(e.LogEventMessage);
            }

            if (content == null)
            {
                return new PackageHashFileFailure(FormattableStringEx.I($"Package hash file is missing."));
            }

            if (content.Length < MinNumberOfLines)
            {
                // The version field potentially can be used for invalidating generated packages as well.
                // The new file format is:
                // Version
                // Specs format version
                // SHA
                // Fingerprint
                // List of files
                return new PackageHashFileFailure(FormattableStringEx.I($"Package hash file has an invalid content. Expected at least {MinNumberOfLines} lines but got {content.Length} lines."));
            }

            var version = content[0];
            if (version != HashFileFormatVersion)
            {
                return new PackageHashFileFailure(FormattableStringEx.I($"Package hash file has different version. Expected version: {HashFileFormatVersion}, actual version: {version}."));
            }

            var specsFileVersion = content[1];

            var fingerprintHash = content[2];
            var fingerprintText = content[3];

            var files = content.Skip(MinNumberOfLines).Where(s => !string.IsNullOrEmpty(s)).ToArray();
            if (files.Length == 0)
            {
                return new PackageHashFileFailure(FormattableStringEx.I($"Package hash file does not have package content files."));
            }

            return new PackageHashFile(fingerprintHash, fingerprintText, files, specsFormatIsUpToDate: specsFileVersion == GeneratedSpecsVersion);
        }

        /// <summary>
        /// Tries to save package's hash file to disk.
        /// </summary>
        public static Possible<Unit> TrySaveTo(string path, PackageHashFile hashFile)
        {
            var sb = new StringBuilder();
            sb.AppendLine(HashFileFormatVersion)
                .AppendLine(GeneratedSpecsVersion)
                .AppendLine(hashFile.FingerprintHash)
                .AppendLine(hashFile.FingerprintText);

            foreach (var file in hashFile.Content)
            {
                sb.AppendLine(file);
            }

            try
            {
                ExceptionUtilities.HandleRecoverableIOException(
                    () =>
                    {
                        File.WriteAllText(path, sb.ToString());
                    },
                    e => throw new BuildXLException("Failed to save a package hash file.", e));

                return Unit.Void;
            }
            catch (BuildXLException e)
            {
                return new PackageHashFileFailure(e.LogEventMessage);
            }
        }

        /// <summary>
        /// Returns the fingeprint hash and the text for logging purposes.
        /// </summary>
        public string FingerprintWithHash()
        {
            return string.Join(Environment.NewLine, FingerprintHash, FingerprintText);
        }
    }
}
