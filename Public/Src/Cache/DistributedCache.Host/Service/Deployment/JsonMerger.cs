// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BuildXL.Cache.Host.Service
{
    public class JsonMerger
    {
        public static string Merge(string json, string overlayJson)
        {
            using (var document = JsonDocument.Parse(json, DeploymentUtilities.ConfigurationDocumentOptions))
            using (var overlayDocument = JsonDocument.Parse(overlayJson, DeploymentUtilities.ConfigurationDocumentOptions))
            using (var stream = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions() { Indented = true }))
                {
                    MergeJsonElement(document.RootElement, overlayDocument.RootElement, writer);
                }

                stream.Position = 0;

                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        private static void MergeJsonElement(JsonElement value, JsonElement? overlay, Utf8JsonWriter writer)
        {
            switch (overlay?.ValueKind ?? value.ValueKind)
            {
                case JsonValueKind.Object:
                    MergeJObject(value, overlay, writer);
                    return;
                default:
                    if (overlay != null)
                    {
                        overlay?.WriteTo(writer);
                    }
                    else
                    {
                        value.WriteTo(writer);
                    }
                    return;
            }
        }

        private static JsonElement? ValidOrNull(JsonElement element)
        {
            return element.ValueKind == JsonValueKind.Undefined ? null : element;
        }

        private static void MergeJObject(JsonElement obj, JsonElement? overlay, Utf8JsonWriter writer)
        {
            writer.WriteStartObject();

            var overlayProperties = new Dictionary<string, JsonElement>();

            if (overlay != null)
            {
                foreach (var p in overlay.Value.EnumerateObject())
                {
                    overlayProperties[p.Name] = p.Value;
                }
            }

            if (obj.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in obj.EnumerateObject())
                {
                    if (overlayProperties.TryGetValue(p.Name, out var overlayProperty))
                    {
                        overlayProperties.Remove(p.Name);
                    }

                    writer.WritePropertyName(p.Name);
                    MergeJsonElement(p.Value, ValidOrNull(overlayProperty), writer);
                }
            }

            if (overlay != null)
            {
                foreach (var p in overlay.Value.EnumerateObject())
                {
                    if (overlayProperties.ContainsKey(p.Name))
                    {
                        p.WriteTo(writer);
                    }
                }
            }

            writer.WriteEndObject();
        }
    }
}
