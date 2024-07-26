// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Linq;
using BuildXL.Cache.BuildCacheResource.Model;
using Xunit;

namespace BuildXL.Cache.BuildCacheResource.Helper.UnitTests
{
    public class BuildCacheResourceTests
    {
        private const string PathToTestJSON = "BuildCacheConfig.json";

        [Fact]
        public void TestWellFormedJSONParsing()
        {
            var configuration = BuildCacheResourceHelper.LoadFromJSONAsync(PathToTestJSON).GetAwaiter().GetResult();

            // CODESYNC: BuildCacheConfig.json
            var caches = configuration.AssociatedBuildCaches.ToList();

            Assert.Equal(2, caches.Count);

            Assert.Equal("MyCache", caches[0].Name);
            Assert.Equal(7, caches[0].RetentionPolicyInDays);

            var shards = caches[0].Shards.ToList();
            Assert.Equal(2, shards.Count);
            Assert.Equal(new Uri("https://nonexistent.storage.account"), shards[0].StorageUri);

            var metadataContainer = shards[0].MetadataContainer;
            var contentContainer = shards[0].ContentContainer;
            var checkpointContainer = shards[0].CheckpointContainer;

            Assert.Equal(BuildCacheContainerType.Metadata, metadataContainer.Type);
            Assert.Equal(BuildCacheContainerType.Content, contentContainer.Type);
            Assert.Equal(BuildCacheContainerType.Checkpoint, checkpointContainer.Type);

            Assert.Equal("MyMetadata", metadataContainer.Name);
            Assert.Equal("MyContent", contentContainer.Name);
            Assert.Equal("MyCheckPoint", checkpointContainer.Name);
        }

        [Fact]
        public void RetentionPolicyIsPositive()
        {
            string json = @"[
  {
  ""Name"": ""MyCache"",
  ""RetentionDays"": 0,
  ""Shards"": [
    {
      ""StorageUrl"": ""https://nonexistent.storage.account"",
      ""Containers"": [
        {
          ""Name"": ""MyMetadata"",
          ""Type"": ""Metadata"",
          ""Signature"": ""?signature=is&valid=true""
        },
        {
          ""Name"": ""MyContent"",
          ""Type"": ""Content"",
          ""Signature"": ""?this=is=some=signature""
        },
        {
          ""Name"": ""MyCheckPoint"",
          ""Type"": ""Checkpoint"",
          ""Signature"": ""?yet=another&signature=true""
        }
      ]
    }
  ]
}
]";
            try
            {
                var configuration = BuildCacheResourceHelper.LoadFromJSONAsync(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json))).GetAwaiter().GetResult();
                Assert.Fail("We should never reach this");
            }
            catch (ArgumentException ex)
            {
                Assert.Contains("retention policy", ex.ToString());
            }
        }

        [Fact]
        public void AtLeastOneShard()
        {
            string json = @"[
 {
  ""Name"": ""MyCache"",
  ""RetentionDays"": 42,
  ""Shards"": []
 }
]";
            try
            {
                var configuration = BuildCacheResourceHelper.LoadFromJSONAsync(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json))).GetAwaiter().GetResult();
                Assert.Fail("We should never reach this");
            }
            catch (ArgumentException ex)
            {
                Assert.Contains("number of shards", ex.ToString());
            }
        }

        [Fact]
        public void AlwaysThreeContainers()
        {
            string json = @"[
 {
  ""Name"": ""MyCache"",
  ""RetentionDays"": 42,
  ""Shards"": [
    {
      ""StorageUrl"": ""https://nonexistent.storage.account"",
      ""Containers"": [
        {
          ""Name"": ""MyMetadata"",
          ""Type"": ""Metadata"",
          ""Signature"": ""?signature=is&valid=true""
        }
      ]
    }
  ]
 }
]";
            try
            {
                var configuration = BuildCacheResourceHelper.LoadFromJSONAsync(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json))).GetAwaiter().GetResult();
                Assert.Fail("We should never reach this");
            }
            catch (ArgumentException ex)
            {
                Assert.Contains("Expected to have three container", ex.ToString());
            }
        }

        [Fact]
        public void AlwaysThreeContainersOfEachType()
        {
            string json = @"[
  {
  ""Name"": ""MyCache"",
  ""RetentionDays"": 42,
  ""Shards"": [
    {
      ""StorageUrl"": ""https://nonexistent.storage.account"",
      ""Containers"": [
        {
          ""Name"": ""MyMetadata"",
          ""Type"": ""Metadata"",
          ""Signature"": ""?signature=is&valid=true""
        },
        {
          ""Name"": ""MyContent"",
          ""Type"": ""Metadata"",
          ""Signature"": ""?this=is=some=signature""
        },
        {
          ""Name"": ""MyCheckPoint"",
          ""Type"": ""Checkpoint"",
          ""Signature"": ""?yet=another&signature=true""
        }
      ]
    }
  ]
}
]";
            try
            {
                var configuration = BuildCacheResourceHelper.LoadFromJSONAsync(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json))).GetAwaiter().GetResult();
                Assert.Fail("We should never reach this");
            }
            catch (ArgumentException ex)
            {
                Assert.Contains("three containers of the types", ex.ToString());
            }
        }
    }
}
