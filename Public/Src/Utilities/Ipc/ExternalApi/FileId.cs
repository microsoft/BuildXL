// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Ipc.ExternalApi
{
    /// <summary>
    /// Helper methods for (de)serializing <see cref="BuildXL.Utilities.FileArtifact"/>.
    /// </summary>
    public static class FileId
    {
        /// <summary>
        /// Serializes a file artifact into a string identifier.
        /// </summary>
        public static string ToString(FileArtifact file)
        {
            return I($"{file.Path.RawValue}:{file.RewriteCount}");
        }

        /// <summary>
        /// Parses a FileId from a string value.  Delegates to <see cref="TryParse"/>;
        /// if unsuccessful, throws <see cref="ArgumentException"/>.
        /// </summary>
        public static FileArtifact Parse(string value)
        {
            FileArtifact result;
            if (!TryParse(value, out result))
            {
                throw new ArgumentException("invalid file id: " + value);
            }

            return result;
        }

        /// <summary>
        /// Attempts to parse a FileArtifact from a string value.
        /// Expected format is {Path.RawValue}:{RewriteCount}.
        /// </summary>
        /// <returns>
        /// Whether the operation succeeded.
        /// </returns>
        public static bool TryParse(string value, out FileArtifact file)
        {
            file = FileArtifact.Invalid;

            string[] splits = value.Split(':');
            if (splits.Length != 2)
            {
                return false;
            }

            int pathId;
            if (!int.TryParse(splits[0], out pathId) || pathId <= 0)
            {
                return false;
            }

            int rewriteCount;
            if (!int.TryParse(splits[1], out rewriteCount) || rewriteCount < 0)
            {
                return false;
            }

            file = new FileArtifact(new AbsolutePath(pathId), rewriteCount);
            return true;
        }
    }
}
