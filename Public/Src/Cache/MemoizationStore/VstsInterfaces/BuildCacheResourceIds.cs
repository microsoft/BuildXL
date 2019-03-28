// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;

namespace BuildXL.Cache.MemoizationStore.VstsInterfaces
{
    /// <summary>
    ///     CODESYNC: This code must match exactly what is found in the VSTS Repo.
    ///     DO NOT CHANGE OR ADD THINGS TO THIS FILE WITHOUT UPDATING VSTS.
    /// </summary>
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public static class BuildCacheResourceIds
    {
#pragma warning disable SA1600 // Elements must be documented
#pragma warning disable 1591
        public const string DefaultCacheNamespace = "default";
        public const string NoneSelectorOutput = "NONESELECTOR";

        /// <summary>
        /// The overall area for Build Cache.
        /// </summary>
        public const string BuildCacheArea = "buildcache";

        /// <summary>
        /// Legacy content bag controller resources.
        /// </summary>
        public const string ContentBagResourceName = "contentbag";
        public const string FingerprintSelectorResourceName = "fingerprintselector";

        /// <summary>
        /// Item based strong/weak fingerprint resources.
        /// </summary>
        public const string SelectorResourceName = "selector";
        public const string ContentHashListResourceName = "contenthashlist";

        /// <summary>
        /// Blob based strong/weak fingerprint resources.
        /// </summary>
        public const string BlobSelectorResourceName = "blobselector";
        public const string BlobContentHashListResourceName = "blobcontenthashlist";

        /// <summary>
        /// General utility cache controller resources.
        /// </summary>
        public const string IncorporateStrongFingerprintsResourceName = "incorporateStrongFingeprint";
        public const string CacheDeterminismGuidResourceName = "cachedeterminismguid";

        /// <summary>
        /// Legacy content bag controller resources.
        /// </summary>
        public static readonly Guid ContentBagResourceId = new Guid("{8ADAC183-0B40-4151-B069-144AC860D516}");
        public static readonly Guid FingerprintSelectorResourceId = new Guid("{46593615-07ae-4034-93a2-8375f7b0b146}");

        /// <summary>
        /// Item based strong/weak fingerprint resources.
        /// </summary>
        public static readonly Guid ContentHashListResourceId = new Guid("{add2fb71-c045-4fe1-b738-257be60417e4}");
        public static readonly Guid SelectorResourceId = new Guid("{57b9cde8-88f8-4731-abc2-bdd8341ea08e}");

        /// <summary>
        /// Item based strong/weak fingerprint resources.
        /// </summary>
        public static readonly Guid BlobContentHashListResourceId = new Guid("{ea205223-2a7d-4ec1-aecf-51bda7b5c883}");
        public static readonly Guid BlobSelectorResourceId = new Guid("{2a90a9a1-dcef-4699-9516-55526085f207}");

        /// <summary>
        /// General utility cache controller resources.
        /// </summary>
        public static readonly Guid IncorporateStrongFingerprintsResourceId = new Guid("{6757d75c-8d63-4538-85ce-580da6a9d566}");
        public static readonly Guid CacheDeterminismGuidResourceId = new Guid("{1ca40a66-8b7a-4610-a12a-a42a669d7916}");
#pragma warning restore 1591
    }
}
