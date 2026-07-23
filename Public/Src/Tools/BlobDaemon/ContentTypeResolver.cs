// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using BuildXL.Utilities.Core;

namespace Tool.BlobDaemon
{
    /// <summary>
    /// Resolves the Content-Type to set on an uploaded blob from a map of file-extension to Content-Type.
    /// A file is matched by the longest extension suffix of its name (so '.tar.gz' wins over '.gz'), and
    /// extension comparison is OS-specific (case-sensitive on Unix, case-insensitive on Windows). When no
    /// entry matches, <see cref="Resolve"/> returns null (the file is still uploaded, just without a Content-Type).
    /// </summary>
    public sealed class ContentTypeResolver
    {
        /// <summary>The configured file-extension to Content-Type mappings (normalized with an OS-specific comparer).</summary>
        public IReadOnlyDictionary<string, string> Mappings { get; }

        /// <nodoc/>
        public ContentTypeResolver(IReadOnlyDictionary<string, string> contentTypeByExtension)
        {
            var comparer = OperatingSystemHelper.IsUnixOS ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
            var map = new Dictionary<string, string>(comparer);
            foreach (var entry in contentTypeByExtension)
            {
                map[entry.Key] = entry.Value;
            }

            Mappings = map;
        }

        /// <summary>
        /// Returns the Content-Type for the file, or null if no extension entry matches.
        /// </summary>
        public string Resolve(string filePath)
        {
            if (Mappings.Count == 0)
            {
                return null;
            }

            var fileName = Path.GetFileName(filePath);

            // Iterate the '.' positions left-to-right: the leftmost dot yields the longest suffix, so the first
            // matching suffix is the longest match.
            for (int i = fileName.IndexOf('.'); i >= 0; i = fileName.IndexOf('.', i + 1))
            {
                if (Mappings.TryGetValue(fileName.Substring(i), out var contentType))
                {
                    return contentType;
                }
            }

            return null;
        }
    }
}
