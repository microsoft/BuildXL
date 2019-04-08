// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Scheduler.Graph
{
    /// <summary>
    /// Mappings from pips (see <see cref="PipId"/>) to their static fingerprints (see <see cref="ContentFingerprint"/>), and vice versa.
    /// </summary>
    public class PipGraphStaticFingerprints
    {
        /// <summary>
        /// Maps from pip static fingerprints to pips.
        /// </summary>
        private readonly ConcurrentBigMap<ContentFingerprint, PipId> m_staticFingerprintsToPips;

        /// <summary>
        /// Maps from pips to their corresponding static hashes.
        /// </summary>
        private readonly ConcurrentBigMap<PipId, ContentFingerprint> m_pipsToStaticFingerprints;

        /// <summary>
        /// Gets all pip static fingerprints.
        /// </summary>
        public IEnumerable<KeyValuePair<PipId, ContentFingerprint>> PipStaticFingerprints => m_pipsToStaticFingerprints;

        /// <summary>
        /// Creates an instance of <see cref="PipGraphStaticFingerprints"/>.
        /// </summary>
        public PipGraphStaticFingerprints()
            : this (new ConcurrentBigMap<ContentFingerprint, PipId>(), new ConcurrentBigMap<PipId, ContentFingerprint>())
        {
        }

        private PipGraphStaticFingerprints(
            ConcurrentBigMap<ContentFingerprint, PipId> staticFingerprintsToPips,
            ConcurrentBigMap<PipId, ContentFingerprint> pipsToStaticFingerprints)
        {
            Contract.Requires(staticFingerprintsToPips != null);
            Contract.Requires(pipsToStaticFingerprints != null);

            m_staticFingerprintsToPips = staticFingerprintsToPips;
            m_pipsToStaticFingerprints = pipsToStaticFingerprints;
        }

        /// <summary>
        /// Adds pip static fingerprint.
        /// </summary>
        /// <param name="pip">Pip.</param>
        /// <param name="fingerprint">Static fingerprint.</param>
        public void AddFingerprint(Pip pip, ContentFingerprint fingerprint)
        {
            Contract.Requires(pip != null);

            m_staticFingerprintsToPips[fingerprint] = pip.PipId;
            m_pipsToStaticFingerprints.Add(pip.PipId, fingerprint);
        }

        /// <summary>
        /// Gets pip's static fingerprints.
        /// </summary>
        /// <param name="pip">Pip.</param>
        /// <param name="fingerprint">Static fingerprint.</param>
        public bool TryGetFingerprint(Pip pip, out ContentFingerprint fingerprint)
        {
            Contract.Requires(pip != null);

            return TryGetFingerprint(pip.PipId, out fingerprint);
        }

        /// <summary>
        /// Gets pip's static fingerprints.
        /// </summary>
        /// <param name="pipId">Pip id.</param>
        /// <param name="fingerprint">Static fingerprint.</param>
        public bool TryGetFingerprint(in PipId pipId, out ContentFingerprint fingerprint)
        {
            Contract.Requires(pipId.IsValid);

            return m_pipsToStaticFingerprints.TryGetValue(pipId, out fingerprint);
        }

        /// <summary>
        /// Gets pip that has a given static fingerprint.
        /// </summary>
        /// <param name="fingerprint">Static fingerprint.</param>
        /// <param name="pipId">Pip id.</param>
        public bool TryGetPip(in ContentFingerprint fingerprint, out PipId pipId)
        {
            pipId = PipId.Invalid;
            return m_staticFingerprintsToPips.TryGetValue(fingerprint, out pipId);
        }

        /// <summary>
        /// Serializes this instance to an instance of <see cref="BuildXLWriter"/>.
        /// </summary>
        /// <param name="writer">An instance of <see cref="BuildXLWriter"/>.</param>
        public void Serialize(BuildXLWriter writer)
        {
            Contract.Requires(writer != null);

            m_staticFingerprintsToPips.Serialize(
                writer,
                kvp =>
                {
                    kvp.Key.WriteTo(writer);
                    kvp.Value.Serialize(writer);
                });

            m_pipsToStaticFingerprints.Serialize(
                writer,
                kvp =>
                {
                    kvp.Key.Serialize(writer);
                    kvp.Value.WriteTo(writer);
                });
        }

        /// <summary>
        /// Deserializes from an instance of <see cref="BuildXLReader"/> to an instance of <see cref="PipGraphStaticFingerprints"/>.
        /// </summary>
        /// <param name="reader">An instance of <see cref="BuildXLReader"/>.</param>
        public static PipGraphStaticFingerprints Deserialize(BuildXLReader reader)
        {
            Contract.Requires(reader != null);

            var staticFingerprintsToPips = ConcurrentBigMap<ContentFingerprint, PipId>.Deserialize(
                reader,
                () => new System.Collections.Generic.KeyValuePair<ContentFingerprint, PipId>(new ContentFingerprint(reader), PipId.Deserialize(reader)));

            var pipsToStaticFingerprints = ConcurrentBigMap<PipId, ContentFingerprint>.Deserialize(
                reader,
                () => new System.Collections.Generic.KeyValuePair<PipId, ContentFingerprint>(PipId.Deserialize(reader), new ContentFingerprint(reader)));

            return new PipGraphStaticFingerprints(staticFingerprintsToPips, pipsToStaticFingerprints);
        }
    }
}
