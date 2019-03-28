// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.Interfaces;
using BuildXL.Storage;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using StrongFingerprint = BuildXL.Cache.Interfaces.StrongFingerprint;

namespace BuildXL.Cache.Tests
{
    /// <summary>
    /// Represents a strong fingerprint that has been generated from a faked build.
    /// </summary>
    public sealed class FakeStrongFingerprint : StrongFingerprint
    {
        /// <summary>
        /// The associated FakeBuild object.
        /// </summary>
        public FakeBuild FakeBuild { get; }

        private FakeStrongFingerprint(WeakFingerprintHash weak, Hash hashElement, FakeBuild fake)
            : base(weak, fake.OutputListHash, hashElement, "FakeCache")
        {
            FakeBuild = fake;
        }

        /// <summary>
        /// Simple helper function to create hash element from string
        /// </summary>
        /// <param name="data">String data to use as input to hash</param>
        /// <returns>The simple hash</returns>
        public static Hash CreateHash(string data)
        {
            return new Hash(GetHashForString(data));
        }

        /// <summary>
        /// Simple helper to create weak fingerprint hash from a string
        /// </summary>
        /// <param name="data">String data to use as input to hash</param>
        /// <returns>The weakfingerprint hash</returns>
        public static WeakFingerprintHash CreateWeak(string data)
        {
            return new WeakFingerprintHash(CreateHash(data));
        }

        /// <summary>
        /// Creates a FakeStrongFingerpint and the associated FakeBuild streams.
        /// </summary>
        /// <param name="pipName">Name of the pip</param>
        /// <param name="generateVerifiablePip">Determines if CheckContentsAsync can verify the contents of the build</param>
        /// <param name="pipSize">Number of files to include in the pip</param>
        /// <remarks>
        /// This method is the StrongFingerprint generation algorithm for FakeBuild.DoNondeterministicPipAsync
        /// </remarks>
        /// <returns>A FakeStrongFingerprint that has the FakeBuild class</returns>
        public static FakeStrongFingerprint Create(string pipName, bool generateVerifiablePip = false, int pipSize = 3)
        {
            FakeBuild fake = new FakeBuild(pipName, pipSize, forceUniqueOutputs: !generateVerifiablePip);
            WeakFingerprintHash weak = CreateWeak(pipName.ToLowerInvariant());
            Hash simpleHash = CreateHash(pipName.ToUpperInvariant());

            return new FakeStrongFingerprint(weak, simpleHash, fake);
        }

        private static Fingerprint GetHashForString(string target)
        {
            return FingerprintUtilities.Hash(target);
        }
    }
}
