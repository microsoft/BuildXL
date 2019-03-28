// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using BuildXL.Cache.ContentStore.VstsInterfaces;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BuildXL.Cache.MemoizationStore.VstsInterfaces
{
#pragma warning disable SA1402 // File may only contain a single class
#pragma warning disable SA1649 // File name must match first type name
    /// <summary>
    ///     Returns data pertaining to the availability of content in the service itself.
    /// </summary>
    public enum ContentAvailabilityGuarantee
    {
        /// <summary>
        ///     All the content required by this content hash list is available within the VSTS service.
        /// </summary>
        AllContentBackedByCache = 0,

        /// <summary>
        ///     No content is available in the build cache service for this content hash list.
        /// </summary>
        NoContentBackedByCache = 100
    }

    /// <summary>
    /// Extension class for content availability guarantee.
    /// </summary>
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public static class ContentAvailabilityGuaranteeExtensions
    {
        /// <summary>
        /// Whether or not an existing guarantee is stronger than a provided guarantee.
        /// </summary>
        public static bool HasStrongerGuarantee(
            this ContentAvailabilityGuarantee existingGuarantee,
            ContentAvailabilityGuarantee otherGuarantee)
        {
            // behaviour: this.HasStrongerGuarantee(that)
            // returns true if this has content backed by cache and that is not.
            // returns false if they are equal or if the other has content backed by cache
            // and this does not.
            return existingGuarantee < otherGuarantee;
        }

        /// <summary>
        /// Tries to get a content guarantee from a serialized string providing a default.
        /// </summary>
        public static ContentAvailabilityGuarantee TryParseOrGetDefault(string value)
        {
            ContentAvailabilityGuarantee guarantee;
            if (Enum.TryParse(value, out guarantee))
            {
                return guarantee;
            }

            return ContentAvailabilityGuarantee.AllContentBackedByCache;
        }
    }

    /// <summary>
    /// represents a ContentHashList with metadata pertaining to the VSTS service.
    /// </summary>
    [DataContract]
    public class ContentHashListWithCacheMetadata
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ContentHashListWithCacheMetadata"/> class.
        /// </summary>
        public ContentHashListWithCacheMetadata(
            ContentHashListWithDeterminism contentHashListWithDeterminism,
            DateTime? contentHashListExpirationUtc,
            ContentAvailabilityGuarantee contentGuarantee,
            byte[] hashOfExistingContentHashList = null)
        {
            if (contentHashListExpirationUtc != null && contentHashListExpirationUtc.Value.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException("Time to live must be an absolute UTC date time.");
            }

            ContentHashListWithDeterminism = contentHashListWithDeterminism;
            ContentHashListExpirationUtc = contentHashListExpirationUtc;
            ContentGuarantee = contentGuarantee;
            HashOfExistingContentHashList = hashOfExistingContentHashList;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ContentHashListWithCacheMetadata"/> class.
        /// </summary>
        public ContentHashListWithCacheMetadata(ContentHashListWithDeterminism contentHashListWithDeterminism, DateTime contentHashListExpirationUtc)
            : this(contentHashListWithDeterminism, contentHashListExpirationUtc, ContentAvailabilityGuarantee.NoContentBackedByCache)
        {
        }

        private ContentHashListWithCacheMetadata()
        {
        }

        /// <summary>
        /// Gets the content hash list.
        /// </summary>
        [DataMember(Name = "ContentHashList")]
        [JsonConverter(typeof(ContentHashListWithDeterminismConverter))]
        public ContentHashListWithDeterminism ContentHashListWithDeterminism { get; private set; }

        /// <summary>
        /// Gets or sets the expiration time in UTC.
        /// </summary>
        [DataMember]
        private DateTime? ContentHashListExpirationUtc { get; set; }

        /// <summary>
        /// Gets or sets the content availability guarantee.
        /// </summary>
        [DataMember]
        public ContentAvailabilityGuarantee ContentGuarantee { get; set; }

        /// <summary>
        /// Gets the ContentHash of the entries that make up this ContentHashlist.
        /// </summary>
        /// <remarks>Used in add or update semantics to replace existing hashlists.</remarks>
        [DataMember]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public byte[] HashOfExistingContentHashList { get; private set; }

        /// <summary>
        /// Returns the effective expiration UTC time factoring in content availability.
        /// </summary>
        public DateTime? GetEffectiveExpirationTimeUtc()
        {
            if (ContentGuarantee == ContentAvailabilityGuarantee.AllContentBackedByCache)
            {
                return ContentHashListExpirationUtc;
            }

            return null;
        }

        /// <summary>
        /// Returns the effective expiration UTC time factoring in content availability.
        /// </summary>
        public DateTime? GetRawExpirationTimeUtc() => ContentHashListExpirationUtc;
    }

    /// <summary>
    /// The service response for cache determinism requests.
    /// </summary>
    [DataContract]
    public class CacheDeterminismResponse
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CacheDeterminismResponse"/> class.
        /// </summary>
        public CacheDeterminismResponse(Guid cacheDeterminism)
        {
            CacheDeterminism = cacheDeterminism;
        }

        /// <summary>
        /// Gets the cache determinism from the service.
        /// </summary>
        [DataMember]
        public Guid CacheDeterminism { get; private set; }
    }

    /// <summary>
    /// The client side request to reset the build cache service's determinism.
    /// </summary>
    [DataContract]
    public class ResetCacheDeterminismRequest
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ResetCacheDeterminismRequest"/> class.
        /// </summary>
        public ResetCacheDeterminismRequest(Guid value)
        {
            ExistingCacheDeterminismGuid = value;
        }

        /// <summary>
        /// Gets the existing determinism that should be replaced.
        /// </summary>
        [DataMember]
        public Guid ExistingCacheDeterminismGuid { get; private set; }
    }

    /// <summary>
    /// The response from the service for a content hash list request.
    /// </summary>
    [DataContract]
    public class ContentHashListResponse
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ContentHashListResponse"/> class.
        /// </summary>
        public ContentHashListResponse(
            ContentHashListWithCacheMetadata contentHashListWithCacheMetadata,
            IDictionary<BlobIdentifier, ExpirableUri> blobIdsToUris)
        {
            ContentHashListWithCacheMetadata = contentHashListWithCacheMetadata;
            BlobDownloadUris = blobIdsToUris?.ToDictionary(
                blobId => blobId.Key.ValueString,
                blobKvp => blobKvp.Value.NotNullUri);
        }

        /// <summary>
        /// Gets the content hash list with cache metadata.
        /// </summary>
        [DataMember]
        public ContentHashListWithCacheMetadata ContentHashListWithCacheMetadata { get; private set; }

        /// <summary>
        /// Gets the download URI mappings for the blobs in a contenthashlist.
        /// </summary>
        [DataMember]
        public IDictionary<string, Uri> BlobDownloadUris { get; private set; }
    }

    /// <summary>
    /// Represents a client side request to incorporate strong fingerprints.
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    [DataContract]
    public struct IncorporateStrongFingerprintsRequest
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="IncorporateStrongFingerprintsRequest"/> struct.
        /// </summary>
        public IncorporateStrongFingerprintsRequest(
            IReadOnlyCollection<StrongFingerprintAndExpiration> strongFingerprintsWithExpiration)
        {
            StrongFingerprints = strongFingerprintsWithExpiration;
        }

        /// <summary>
        /// Gets the set of fingerprints and when to expire them.
        /// </summary>
        [DataMember]
        public IReadOnlyCollection<StrongFingerprintAndExpiration> StrongFingerprints { get; private set; }
    }

    /// <summary>
    /// Represents a container for the strong fingerprint and the expiration of the fingerprint.
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    [DataContract]
    public struct StrongFingerprintAndExpiration
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="StrongFingerprintAndExpiration"/> struct.
        /// </summary>
        public StrongFingerprintAndExpiration(StrongFingerprint strongFingerprint, DateTime expirationDateUtc)
        {
            if (expirationDateUtc.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException("Time to live must be a UTC date time.");
            }

            StrongFingerprint = strongFingerprint;
            ExpirationDateUtc = expirationDateUtc;
        }

        /// <summary>
        /// Gets the strong fingerprint.
        /// </summary>
        [JsonConverter(typeof(StrongFingerprintConverter))]
        [DataMember]
        public StrongFingerprint StrongFingerprint { get; private set; }

        /// <summary>
        /// Gets the expiration UTC for a strong fingerprint.
        /// </summary>
        [DataMember]
        public DateTime ExpirationDateUtc { get; private set; }
    }

    /// <summary>
    /// Gets the response to a selectors request from the service.
    /// </summary>
    [DataContract]
    public class SelectorsResponse
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SelectorsResponse"/> class.
        /// </summary>
        public SelectorsResponse(
            IReadOnlyList<SelectorAndPossibleContentHashListResponse> selectorsAndPossibleContentHashLists)
        {
            SelectorsAndPossibleContentHashLists = selectorsAndPossibleContentHashLists;
        }

        /// <summary>
        /// Gets the selectors and possible content hash lists.
        /// </summary>
        [DataMember]
        public IReadOnlyList<SelectorAndPossibleContentHashListResponse> SelectorsAndPossibleContentHashLists { get; private set; }
    }

    /// <summary>
    /// Selector and Possible Content Hash lists returned by the service.
    /// </summary>
    [DataContract]
    public class SelectorAndPossibleContentHashListResponse
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SelectorAndPossibleContentHashListResponse"/> class.
        /// </summary>
        public SelectorAndPossibleContentHashListResponse(
            Selector selector,
            ContentHashListResponse contentHashList)
        {
            Selector = selector;
            ContentHashList = contentHashList;
        }

        /// <summary>
        /// Gets the selector.
        /// </summary>
        [JsonConverter(typeof(SelectorConverter))]
        [DataMember]
        public Selector Selector { get; private set; }

        /// <summary>
        /// Gets the content hash list.
        /// </summary>
        [DataMember]
        public ContentHashListResponse ContentHashList { get; private set; }
    }

    /// <summary>
    /// Client side requests for add content hash list.
    /// </summary>
    [DataContract]
    public class AddContentHashListRequest
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AddContentHashListRequest"/> class.
        /// </summary>
        public AddContentHashListRequest(ContentHashListWithCacheMetadata contentHashListWithCacheMetadata)
        {
            ContentHashListWithCacheMetadata = contentHashListWithCacheMetadata;
        }

        /// <summary>
        /// Gets the content hash list with cache metadata.
        /// </summary>
        [DataMember]
        public ContentHashListWithCacheMetadata ContentHashListWithCacheMetadata { get; private set; }
    }

    /// <summary>
    /// ContentHashlists have a property for 'ContentHash'
    /// which is an unsafe cloudstore datatype that doesn't allow for serialization
    /// through JSON.
    /// To pass through any cache datastructures from client to server, we need to
    /// serialize them in a JSON friendly way which involves creating a converter for
    /// the purpose.
    /// </summary>
    public class ContentHashListWithDeterminismConverter : JsonConverter
    {
        private const string PayloadFieldName = "Payload";
        private const string DeterminismFieldName = "Determinism";
        private const string ExpirationUtcFieldName = "ExpirationUtc";
        private const string HashesFieldName = "Hashes";

        /// <inheritdoc />
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var contentHashListWithDeterminism = (ContentHashListWithDeterminism)value;
            var contentHashList = contentHashListWithDeterminism.ContentHashList;
            var determinism = contentHashListWithDeterminism.Determinism;
            writer.WriteStartObject();
            if (contentHashList.HasPayload)
            {
                writer.WritePropertyName(PayloadFieldName);
                serializer.Serialize(
                    writer,
                    HexUtilities.BytesToHex(contentHashList.Payload.ToList()));
            }

            writer.WritePropertyName(DeterminismFieldName);
            serializer.Serialize(writer, determinism.Guid.ToString());

            writer.WritePropertyName(ExpirationUtcFieldName);
            serializer.Serialize(writer, determinism.ExpirationUtc.ToBinary());

            writer.WritePropertyName(HashesFieldName);
            var hashes = contentHashList.Hashes.Select(hash => hash.SerializeReverse()).ToList();
            serializer.Serialize(writer, hashes, typeof(List<string>));

            writer.WriteEndObject();
        }

        /// <inheritdoc />
        public override object ReadJson(
            JsonReader reader,
            Type objectType,
            object existingValue,
            JsonSerializer serializer)
        {
            JObject jsonObject = JObject.Load(reader);

            byte[] payload = null;
            JToken payloadValue;
            if (jsonObject.TryGetValue(PayloadFieldName, StringComparison.Ordinal, out payloadValue))
            {
                payload = HexUtilities.HexToBytes(payloadValue.Value<string>());
            }

            JToken expirationUtcToken;
            var expirationUtc = jsonObject.TryGetValue(ExpirationUtcFieldName, StringComparison.Ordinal, out expirationUtcToken)
                ? DateTime.FromBinary(expirationUtcToken.Value<long>())
                : CacheDeterminism.Expired;
            CacheDeterminism determinism =
                CacheDeterminism.ViaCache(
                    Guid.Parse(jsonObject.GetValue(DeterminismFieldName, StringComparison.Ordinal).Value<string>()),
                    expirationUtc);

            var contentHashes = new List<ContentHash>();
            foreach (
                string contentHashString in jsonObject.GetValue(HashesFieldName, StringComparison.Ordinal).Values<string>())
            {
                ContentHash deserializedContentHash;
                if (!ContentHash.TryParse(contentHashString, out deserializedContentHash))
                {
                    throw new InvalidDataException("Unable to parse hash out of JSON Token");
                }

                contentHashes.Add(deserializedContentHash);
            }

            var contentHashList = new ContentHashList(contentHashes.ToArray(), payload);

            return new ContentHashListWithDeterminism(contentHashList, determinism);
        }

        /// <inheritdoc />
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(ContentHashListWithDeterminism);
        }
    }

    /// <summary>
    /// Selectors have a property for 'ContentHash'
    /// which is an unsafe cloudstore datatype that doesn't allow for serialization
    /// through JSON.
    /// To pass through any cache datastructures from client to server, we need to
    /// serialize them in a JSON friendly way which involves creating a converter for
    /// the purpose.
    /// </summary>
    public class SelectorConverter : JsonConverter
    {
        private const string OutputFieldName = "Output";
        private const string ContentHashFieldName = "ContentHash";

        /// <inheritdoc />
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var selector = (Selector)value;
            writer.WriteStartObject();
            if (selector.Output != null)
            {
                writer.WritePropertyName(OutputFieldName);
                serializer.Serialize(
                    writer,
                    HexUtilities.BytesToHex(selector.Output.ToList()));
            }

            writer.WritePropertyName(ContentHashFieldName);
            serializer.Serialize(writer, selector.ContentHash.SerializeReverse());

            writer.WriteEndObject();
        }

        /// <inheritdoc />
        public override object ReadJson(
            JsonReader reader,
            Type objectType,
            object existingValue,
            JsonSerializer serializer)
        {
            return GetSelectorFromJReader(reader);
        }

        /// <summary>
        /// Gets a selector object from the JSON reader.
        /// </summary>
        internal object GetSelectorFromJReader(JsonReader reader)
        {
            JObject jsonObject = JObject.Load(reader);

            byte[] output = null;
            JToken outputToken;
            if (jsonObject.TryGetValue(OutputFieldName, StringComparison.Ordinal, out outputToken))
            {
                output = HexUtilities.HexToBytes(outputToken.Value<string>());
            }

            var contentHashString = jsonObject.GetValue(ContentHashFieldName, StringComparison.Ordinal).Value<string>();
            ContentHash deserializedContentHash;
            if (!ContentHash.TryParse(contentHashString, out deserializedContentHash))
            {
                throw new InvalidDataException("Unable to parse hash out of JSON Token");
            }

            return new Selector(deserializedContentHash, output);
        }

        /// <inheritdoc />
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(Selector);
        }
    }

    /// <summary>
    /// StrongFingerprints have selectors that have a property for 'ContentHash'
    /// which is an unsafe cloudstore datatype that doesn't allow for serialization
    /// through JSON.
    /// To pass through any cache datastructures from client to server, we need to
    /// serialize them in a JSON friendly way which involves creating a converter for
    /// the purpose.
    /// </summary>
    public class StrongFingerprintConverter : JsonConverter
    {
        private const string WeakFingerprintFieldName = "WeakFingerprint";
        private const string SelectorFieldName = "Selector";
        private readonly SelectorConverter _selectorConverter = new SelectorConverter();

        /// <inheritdoc />
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var strongFingerprint = (StrongFingerprint)value;
            writer.WriteStartObject();
            writer.WritePropertyName(WeakFingerprintFieldName);
            serializer.Serialize(
                writer,
                HexUtilities.BytesToHex(strongFingerprint.WeakFingerprint.ToByteArray()));

            writer.WritePropertyName(SelectorFieldName);
            _selectorConverter.WriteJson(writer, strongFingerprint.Selector, serializer);
            writer.WriteEndObject();
        }

        /// <inheritdoc />
        public override object ReadJson(
            JsonReader reader,
            Type objectType,
            object existingValue,
            JsonSerializer serializer)
        {
            JObject jsonObject = JObject.Load(reader);

            byte[] output;
            JToken outputToken;
            if (jsonObject.TryGetValue(WeakFingerprintFieldName, StringComparison.Ordinal, out outputToken))
            {
                output = HexUtilities.HexToBytes(outputToken.Value<string>());
            }
            else
            {
                throw new ArgumentException("Invalid json for a Strong Fingerprint: no WeakFingerprint found.");
            }

            JToken selectorToken;
            Selector selector;
            if (jsonObject.TryGetValue(SelectorFieldName, out selectorToken))
            {
                selector = (Selector)_selectorConverter.GetSelectorFromJReader(selectorToken.CreateReader());
            }
            else
            {
                throw new ArgumentException("Invalid JSON for Strong Fingerprint. no selector found");
            }

            return new StrongFingerprint(new Fingerprint(output), selector);
        }

        /// <inheritdoc />
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(StrongFingerprint);
        }
    }
}
