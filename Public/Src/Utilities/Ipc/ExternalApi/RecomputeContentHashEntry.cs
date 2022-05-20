// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Cache.ContentStore.Hashing;

namespace BuildXL.Ipc.ExternalApi
{
    /// <summary>
    /// A struct represents data requred to perform recomputing content hash
    /// </summary>
    public class RecomputeContentHashEntry
    {
        /// <summary>
        /// Full file path on disk. Used if file needs to be materialized.
        /// </summary>
        public readonly string FullPath;

        /// <summary>
        /// Current computed contnent hash, used as key for mapping to recomputed content hash
        /// </summary>
        public readonly ContentHash Hash;

        /// <nodoc/>
        public RecomputeContentHashEntry(string fullPath, ContentHash hash)
        {
            Contract.RequiresNotNull(fullPath);
            Contract.Requires(hash.IsValid);

            FullPath = fullPath;
            Hash = hash;
        }

        /// <nodoc/>
        public static RecomputeContentHashEntry Deserialize(BinaryReader reader)
        {
            string path = reader.ReadString();
            var newhash = new ContentHash(reader);
            return new RecomputeContentHashEntry(path, newhash);
        }

        /// <nodoc/>
        public void Serialize(BinaryWriter writer)
        {
            writer.Write(FullPath);
            Hash.Serialize(writer);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return string.Format("{0}|{1}", FullPath, Hash);
        }
    }
}
