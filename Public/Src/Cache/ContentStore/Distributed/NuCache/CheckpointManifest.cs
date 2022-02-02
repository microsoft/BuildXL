// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Checkpoint state obtained from the central store.
    /// </summary>
    /// <remarks>
    /// This is not a record because .NET Core 3.1 does not support specifying constructors.
    /// 
    /// See: https://docs.microsoft.com/en-us/dotnet/standard/serialization/system-text-json-immutability?pivots=dotnet-5-0
    /// </remarks>
    public class CheckpointManifest
    {
        /// <nodoc />
        [JsonIgnore]
        private KeyedList<ShortHash, ContentEntry> Content { get; set; } = new();

        private KeyedList<string, ContentEntry> _contentByPath = new KeyedList<string, ContentEntry>();
        /// <nodoc />
        public KeyedList<string, ContentEntry> ContentByPath
        {
            get => _contentByPath;
            set
            {
                _contentByPath = value;
                Recompute();
            }
        }

        /// <nodoc />
        [JsonIgnore]
        public long TotalSize { get; set; }

        /// <nodoc />
        [JsonIgnore]
        public bool MissingContentInfo { get; set; }

        /// <nodoc />
        [JsonIgnore]
        public bool MissingSizeInfo { get; set; }

        private void Recompute()
        {
            if (ContentByPath != null)
            {
                foreach (var entry in ContentByPath)
                {
                    AddCore(entry);
                }
            }
        }

        public void Add(ContentEntry entry)
        {
            if (ContentByPath.TryAdd(entry))
            {
                AddCore(entry);
            }
        }

        private void AddCore(ContentEntry entry)
        {
            if (entry.Hash.HashType != HashType.Unknown)
            {
                Content.TryAdd(entry);
            }
            else
            {
                MissingContentInfo = true;
            }

            if (entry.Size > 0)
            {
                TotalSize += entry.Size;
            }
            else
            {
                MissingSizeInfo = true;
            }
        }

        public bool TryGetValue(string relativePath, [NotNullWhen(true)] out string? storageId)
        {
            if (ContentByPath.TryGetValue(relativePath, out var entry))
            {
                storageId = entry.StorageId;
                return true;
            }
            else
            {
                storageId = null;
                return false;
            }
        }

        public bool TryGetEntry(ShortHash hash, out ContentEntry entry)
        {
            return Content.TryGetValue(hash, out entry);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"Content={ContentByPath.Count} TotalSizeMb={ByteSizeFormatter.ToMegabytes((ulong)TotalSize)} MissingContentInfo={MissingContentInfo} MissingSizeInfo={MissingSizeInfo}";
        }

        public record struct ContentEntry : IKeyedItem<ShortHash>, IKeyedItem<string>
        {
            public ShortHash Hash { get; set; }
            public string RelativePath { get; set; }
            public string StorageId { get; set; }
            public long Size { get; set; }

            public ShortHash GetKey()
            {
                return Hash;
            }

            string IKeyedItem<string>.GetKey()
            {
                return RelativePath;
            }
        }
    }
}
