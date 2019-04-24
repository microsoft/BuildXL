// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.Interfaces;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Storage;
using BuildXL.Utilities;
using CacheDeterminism = BuildXL.Cache.MemoizationStore.Interfaces.Sessions.CacheDeterminism;

namespace BuildXL.Cache.MemoizationStoreAdapter
{
    internal static class ConversionExtensions
    {
        public static ContentHash ToMemoization(in this CasHash hash)
        {
            return ContentHashingUtilities.CreateFrom(hash.ToArray());
        }

        public static Fingerprint ToMemoization(in this WeakFingerprintHash weak)
        {
            return weak.FingerprintHash.RawHash;
        }

        public static ContentHashListWithDeterminism ToMemoization(in this CasEntries hashes)
        {
            return new ContentHashListWithDeterminism(
                new ContentHashList(hashes.Select(hash => hash.ToMemoization()).ToArray()),
                hashes.Determinism.ToMemoization());
        }

        internal static CacheDeterminism ToMemoization(in this BuildXL.Cache.Interfaces.CacheDeterminism determinism)
        {
            if (determinism.IsDeterministicTool)
            {
                return CacheDeterminism.Tool;
            }

            if (determinism.IsSinglePhaseNonDeterministic)
            {
                return CacheDeterminism.SinglePhaseNonDeterministic;
            }

            // May not be necessary ?
            if (!determinism.IsDeterministic)
            {
                return CacheDeterminism.None;
            }

            return CacheDeterminism.ViaCache(determinism.Guid, determinism.ExpirationUtc);
        }

        [SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly")]
        public static Possible<string, Failure> FromMemoization(this PinResult result, CasHash hash, string cacheId)
        {
            switch (result.Code)
            {
                case PinResult.ResultCode.Success:
                    return cacheId;
                case PinResult.ResultCode.ContentNotFound:
                    return new NoCasEntryFailure(cacheId, hash);
                case PinResult.ResultCode.Error:
                    return new CacheFailure(result.ErrorMessage);
                default:
                    return new CacheFailure("Unrecognized PinResult code: " + result.Code + ", error message: " + (result.ErrorMessage ?? string.Empty));
            }
        }

        [SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly")]
        public static FileRealizationMode ToMemoization(this FileState fileState)
        {
            switch (fileState)
            {
                case FileState.Writeable:
                    return FileRealizationMode.CopyNoVerify;
                case FileState.ReadOnly:
                    return FileRealizationMode.Any;
                default:
                    throw new NotImplementedException("Unrecognized FileState: " + fileState);
            }
        }

        public static CasHash FromMemoization(in this ContentHash hash)
        {
            return new CasHash(new Hash(hash));
        }

        public static CasEntries FromMemoization(in this ContentHashListWithDeterminism contentHashListWithDeterminism)
        {
            return new CasEntries(
                contentHashListWithDeterminism.ContentHashList.Hashes.Select(contentHash => contentHash.FromMemoization()),
                contentHashListWithDeterminism.Determinism.FromMemoization());
        }

        internal static BuildXL.Cache.Interfaces.CacheDeterminism FromMemoization(in this CacheDeterminism determinism)
        {
            if (determinism.IsDeterministicTool)
            {
                return BuildXL.Cache.Interfaces.CacheDeterminism.Tool;
            }

            if (determinism.IsSinglePhaseNonDeterministic)
            {
                return BuildXL.Cache.Interfaces.CacheDeterminism.SinglePhaseNonDeterministic;
            }

            // May not be necessary ?
            if (!determinism.IsDeterministic)
            {
                return BuildXL.Cache.Interfaces.CacheDeterminism.None;
            }

            return BuildXL.Cache.Interfaces.CacheDeterminism.ViaCache(determinism.Guid, determinism.ExpirationUtc);
        }

        internal static Possible<BuildXL.Cache.Interfaces.StrongFingerprint, Failure> FromMemoization(
            this GetSelectorResult selectorResult, WeakFingerprintHash weak, string cacheId)
        {
            if (selectorResult.Succeeded)
            {
                return new Possible<BuildXL.Cache.Interfaces.StrongFingerprint, Failure>(new BuildXL.Cache.Interfaces.StrongFingerprint(
                    weak,
                    selectorResult.Selector.ContentHash.FromMemoization(),
                    new Hash(FingerprintUtilities.CreateFrom(selectorResult.Selector.Output)),
                    cacheId));
            }
            else
            {
                return new Possible<BuildXL.Cache.Interfaces.StrongFingerprint, Failure>(new CacheFailure(selectorResult.ErrorMessage));
            }
        }

        internal static BuildXL.Cache.Interfaces.StrongFingerprint FromMemoization(
            in this BuildXL.Cache.MemoizationStore.Interfaces.Sessions.StrongFingerprint strongFingerprint,
            string cacheId)
        {
            var weak = new WeakFingerprintHash(strongFingerprint.WeakFingerprint.ToByteArray());
            return new BuildXL.Cache.Interfaces.StrongFingerprint(
                    weak,
                    strongFingerprint.Selector.ContentHash.FromMemoization(),
                    new Hash(FingerprintUtilities.CreateFrom(strongFingerprint.Selector.Output)),
                    cacheId);
        }
    }
}
