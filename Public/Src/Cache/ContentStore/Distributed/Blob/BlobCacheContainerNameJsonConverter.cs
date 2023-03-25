// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Blob;

public class BlobCacheContainerNameJsonConverter : JsonConverter<BlobCacheContainerName>
{
    public override BlobCacheContainerName Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return BlobCacheContainerName.Parse(reader.GetString()!);
    }

    public override void Write(Utf8JsonWriter writer, BlobCacheContainerName containerName, JsonSerializerOptions options)
    {
        writer.WriteStringValue(containerName.ToString());
    }
}
