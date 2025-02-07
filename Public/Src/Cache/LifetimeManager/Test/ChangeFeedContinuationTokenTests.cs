// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text.Json;
using BuildXL.Cache.BlobLifetimeManager.Library;
using Xunit;

namespace BuildXL.Cache.BlobLifetimeManager.Test
{
    public class ChangeFeedContinuationTokenTests
    {
        private const string SampleJson = @"
        {
            ""CursorVersion"": 1,
            ""UrlHost"": ""w5piseikjw00003blobl3.z30.blob.storage.azure.net"",
            ""EndTime"": null,
            ""CurrentSegmentCursor"": {
                ""ShardCursors"": [
                    {
                        ""CurrentChunkPath"": ""log/00/2025/01/16/2000/00000.avro"",
                        ""BlockOffset"": 401762300,
                        ""EventIndex"": 59
                    }
                ],
                ""CurrentShardPath"": ""log/00/2025/01/16/2000/"",
                ""SegmentPath"": ""idx/segments/2025/01/16/2000/meta.json""
            }
        }";

        [Fact]
        public void Deserialize_ValidJson_CreatesExpectedObject()
        {
            var token = JsonSerializer.Deserialize<ChangeFeedContinuationToken>(SampleJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            Assert.NotNull(token);
            Assert.Equal(1, token.CursorVersion);
            Assert.Equal("w5piseikjw00003blobl3.z30.blob.storage.azure.net", token.UrlHost);
            Assert.Null(token.EndTime);
            Assert.NotNull(token.CurrentSegmentCursor);
            Assert.Single(token.CurrentSegmentCursor.ShardCursors);
            Assert.Equal("log/00/2025/01/16/2000/", token.CurrentSegmentCursor.CurrentShardPath);
            Assert.Equal("idx/segments/2025/01/16/2000/meta.json", token.CurrentSegmentCursor.SegmentPath);
        }

        [Fact]
        public void SegmentDate_ValidSegmentPath_ReturnsCorrectDate()
        {
            var cursor = new CurrentSegmentCursor
            {
                ShardCursors = new List<ShardCursor>
                {
                    new ShardCursor
                    {
                        CurrentChunkPath = "log/00/2025/01/16/2000/00000.avro",
                        BlockOffset = 401762300,
                        EventIndex = 59
                    }
                },
                CurrentShardPath = "log/00/2025/01/16/2000/",
                SegmentPath = "idx/segments/2025/01/16/2000/meta.json"
            };

            var segmentDate = cursor.SegmentDate;

            Assert.NotNull(segmentDate);
            Assert.Equal(new DateTime(2025, 1, 16), segmentDate);
        }

        [Fact]
        public void SegmentDate_InvalidSegmentPath_ReturnsNull()
        {
            var cursor = new CurrentSegmentCursor
            {
                ShardCursors = new List<ShardCursor>(),
                CurrentShardPath = "log/00/2025/01/16/2000/",
                SegmentPath = "invalid/path/without/date/meta.json"
            };

            var segmentDate = cursor.SegmentDate;

            Assert.Null(segmentDate);
        }

        [Fact]
        public void SegmentDate_EmptySegmentPath_ReturnsNull()
        {
            var cursor = new CurrentSegmentCursor
            {
                ShardCursors = new List<ShardCursor>(),
                CurrentShardPath = "log/00/2025/01/16/2000/",
                SegmentPath = ""
            };

            var segmentDate = cursor.SegmentDate;

            Assert.Null(segmentDate);
        }
    }
}
