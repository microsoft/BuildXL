// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.Interfaces;
using Test.BuildXL.TestUtilities.Xunit;

namespace BuildXL.Cache.Tests
{
    /// <summary>
    /// A helper struct for fake building operations in the cache tests
    /// </summary>
    public readonly struct PipDefinition
    {
        /// <summary>
        /// Name of the pip
        /// </summary>
        public readonly string PipName;

        /// <summary>
        /// Size (number of outputs)
        /// </summary>
        public readonly int PipSize;

        /// <summary>
        /// Index of the output used as the weak hash
        /// </summary>
        public readonly int WeakIndex;

        /// <summary>
        /// Index of the output used as the hash
        /// </summary>
        public readonly int HashIndex;

        /// <summary>
        /// The determinism to use
        /// </summary>
        public readonly CacheDeterminism Determinism;

        /// <summary>
        /// Simple constructor of a fake pip definition.
        /// </summary>
        /// <param name="pipName">Require pip name (used as data too)</param>
        /// <param name="pipSize">Number of outputs</param>
        /// <param name="weakIndex">Output index used to produce the weak fingerprint</param>
        /// <param name="hashIndex">Output index used to produce the hash</param>
        /// <param name="determinism">The determinism to claim for new records</param>
        public PipDefinition(string pipName, int pipSize = 3, int weakIndex = 1, int hashIndex = 0, CacheDeterminism determinism = default(CacheDeterminism))
        {
            Contract.Requires(pipName != null);
            Contract.Requires(pipSize > 0);
            Contract.Requires(weakIndex >= 0 && weakIndex < pipSize);
            Contract.Requires(hashIndex >= 0 && hashIndex < pipSize);

            PipName = pipName;
            PipSize = pipSize;
            WeakIndex = weakIndex;
            HashIndex = hashIndex;
            Determinism = determinism;
        }

        /// <summary>
        /// Execute a fake build based on this pip definition into the given cache session
        /// </summary>
        /// <param name="session">The cache session to use</param>
        /// <returns>The FullCacheRecord of the build operation</returns>
        public async Task<FullCacheRecord> BuildAsync(ICacheSession session)
        {
            Contract.Requires(session != null);

            FullCacheRecord record = await FakeBuild.DoPipAsync(
                session,
                pipName: PipName,
                pipSize: PipSize,
                weakIndex: WeakIndex,
                hashIndex: HashIndex,
                determinism: Determinism);

            if (Determinism.IsDeterministicTool)
            {
                XAssert.IsTrue(record.CasEntries.Determinism.IsDeterministicTool, "Tool was supposed to be deterministic");
            }

            return record;
        }
    }
}
