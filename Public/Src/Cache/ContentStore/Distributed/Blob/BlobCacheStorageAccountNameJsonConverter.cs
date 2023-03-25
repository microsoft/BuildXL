// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Blob;

public class BlobCacheStorageAccountNameJsonConverter : JsonConverter<BlobCacheStorageAccountName>
{
    public override BlobCacheStorageAccountName Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return BlobCacheStorageAccountName.Parse(reader.GetString()!);
    }

    public override void Write(Utf8JsonWriter writer, BlobCacheStorageAccountName accountName, JsonSerializerOptions options)
    {
        writer.WriteStringValue(accountName.ToString());
    }
}
