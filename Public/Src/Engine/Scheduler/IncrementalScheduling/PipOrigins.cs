// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using static BuildXL.Scheduler.IncrementalScheduling.IncrementalSchedulingStateWriteTextHelpers;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Scheduler.IncrementalScheduling
{
    /// <summary>
    /// Class for tracking origins of pips in graph-agnostic incremental scheduling.
    /// </summary>
    internal sealed class PipOrigins
    {
        /// <summary>
        /// Mappings from pip fingerprints to their semi-stable hash, (index of) origin pip graph, and pip id.
        /// </summary>
        private readonly ConcurrentBigMap<ContentFingerprint, (long semiStableHash, int pipGraphIndex)> m_pipOrigins;

        /// <summary>
        /// Creates a new instance of <see cref="PipOrigins"/>.
        /// </summary>
        public static PipOrigins CreateNew() => new PipOrigins(new ConcurrentBigMap<ContentFingerprint, (long semiStableHash, int pipGraphIndex)>());

        private PipOrigins(ConcurrentBigMap<ContentFingerprint, (long semiStableHash, int pipGraphIndex)> pipOrigins)
        {
            Contract.Requires(pipOrigins != null);

            m_pipOrigins = pipOrigins;
        }

        private static int GetPipOriginsIndex(PipStableId pipStableId) => (int)pipStableId;

        private PipStableId CreateStableId(int pipOriginsIndex) => (PipStableId)pipOriginsIndex;

        /// <summary>
        /// Tries to get fingerprint by pip id.
        /// </summary>
        public bool TryGetFingerprint(PipStableId pipId, out ContentFingerprint fingerprint)
        {
            var backingEntries = m_pipOrigins.BackingSet.GetItemsUnsafe();
            var originsIndex = GetPipOriginsIndex(pipId);
            if (originsIndex < backingEntries.Capacity)
            {
                var entry = backingEntries[originsIndex];
                fingerprint = entry.Key;
                return !fingerprint.IsDefault;
            }

            fingerprint = default;
            return false;
        }

        /// <summary>
        /// Tries to get pip id by fingerprint.
        /// </summary>
        public bool TryGetPipId(in ContentFingerprint fingerprint, out PipStableId pipId)
        {
            pipId = PipStableId.Invalid;
            var getResult = m_pipOrigins.TryGet(fingerprint);
            if (getResult.IsFound)
            {
                pipId = CreateStableId(getResult.Index);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Tries to add origin.
        /// </summary>
        public PipStableId GetOrAddOrigin(in ContentFingerprint fingerprint, (long semiStableHash, int pipGraphIndex) origin)
        {
            var result = m_pipOrigins.GetOrAdd(fingerprint, origin);
            return CreateStableId(result.Index);
        }

        /// <summary>
        /// Updates pip origin, and gets back pip id.
        /// </summary>
        public void Update(in ContentFingerprint fingerprint, (long semiStableHash, int pipGraphIndex) origin) => m_pipOrigins[fingerprint] = origin;

        /// <summary>
        /// Adds or updates pip origin, and gets back pip id.
        /// </summary>
        public PipStableId AddOrUpdate(in ContentFingerprint fingerprint, (long semiStableHash, int pipGraphIndex) origin)
        {
            var result = m_pipOrigins.AddOrUpdate(
                fingerprint,
                origin,
                (fp, o) => o,
                (fp, o, e) => o);

            return CreateStableId(result.Index);
        }

        /// <summary>
        /// Removes origin by <see cref="PipStableId"/>.
        /// </summary>
        public bool TryRemove(PipStableId pipId, out (long semiStableHash, int pipGraphIndex) origin)
        {
            origin = default;
            return TryGetFingerprint(pipId, out ContentFingerprint fingerprint) ? TryRemove(fingerprint, out origin) : false;
        }

        /// <summary>
        /// Removes origin by fingerprint.
        /// </summary>
        public bool TryRemove(in ContentFingerprint fingerprint, out (long semiStableHash, int pipGraphIndex) origin) => m_pipOrigins.TryRemove(fingerprint, out origin);

        /// <summary>
        /// Serializes to an instance of <see cref="BuildXLWriter"/>.
        /// </summary>
        public void Serialize(BuildXLWriter writer)
        {
            Contract.Requires(writer != null);

            var buffer = new byte[FingerprintUtilities.FingerprintLength];

            m_pipOrigins.Serialize(
                writer,
                kvp =>
                {
                    writer.Write(kvp.Key, buffer);
                    writer.Write(kvp.Value.semiStableHash);
                    writer.Write(kvp.Value.pipGraphIndex);
                });
        }

        /// <summary>
        /// Deserailizes from an instance of <see cref="BuildXLReader"/>.
        /// </summary>
        public static PipOrigins Deserialize(BuildXLReader reader)
        {
            var buffer = new byte[FingerprintUtilities.FingerprintLength];

            var pipOrigins = ConcurrentBigMap<ContentFingerprint, (long semiStableHash, int pipGraphIndex)>.Deserialize(
                reader,
                () =>
                {
                    var kvp = new KeyValuePair<ContentFingerprint, (long semiStableHash, int pipGraphIndex)>(
                        reader.ReadContentFingerprint(buffer),
                        (reader.ReadInt64(), reader.ReadInt32()));
                    return kvp;
                });

            return new PipOrigins(pipOrigins);
        }

        public void WriteText(TextWriter writer)
        {
            WriteTextEntryWithBanner(
                writer,
                "Pip origins",
                w =>
                {
                    WriteTextEntryWithHeader(
                        w,
                        "Pip stable ids to fingerprints",
                        w1 =>
                        {
                            var backingEntries = m_pipOrigins.BackingSet.GetItemsUnsafe();
                            var capacity = backingEntries.Capacity;
                            for (int i = 0; i < capacity; i++)
                            {
                                var fingerprintAndOrigin = backingEntries[i];

                                // Check if entry is default entry (i.e., entry was removed from map)
                                if (!fingerprintAndOrigin.Key.IsDefault || fingerprintAndOrigin.Value.semiStableHash != 0)
                                {
                                    w1.WriteLine(I($"PIP_ID: {i:X16} => FP:{fingerprintAndOrigin.Key.ToString()} => Pip{fingerprintAndOrigin.Value.semiStableHash:X16}, Graph Index: {fingerprintAndOrigin.Value.pipGraphIndex}"));
                                }
                            }
                        });
                });
        }
    }
}
