// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Cache.BasicFilesystem;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace BuildXL.Cache.Tests
{
    public class TestBasicFilesystemSharded : TestBasicFilesystem
    {
        /// <summary>
        /// Number of partitions/shards that the cache is split into
        /// </summary>
        public const int SHARD_COUNT = 8;

        /// <summary>
        /// Method responsible for providing the string used in the shard file.
        /// </summary>
        /// <remarks>
        /// XUnit is nice enough to run each test in a seperate instantiation of the class,
        /// so no need to worry about resetting this.
        /// </remarks>
        private Func<string, string> m_shardFileSource = WriteShardFileDefault;

        private static string WriteShardFileDefault(string cacheDir)
        {
            string[] shardRoots = new string[SHARD_COUNT * 2];
            StringBuilder writer = new StringBuilder();

            for (int i = 0; i < SHARD_COUNT * 2; i++)
            {
                shardRoots[i] = cacheDir + ".s." + i;
            }

            writer.AppendLine(BuildXL.Cache.BasicFilesystem.BasicFilesystemCache.WFP_TOKEN);
            for (int i = 0; i < SHARD_COUNT; i++)
            {
                writer.AppendLine(shardRoots[i]);
            }

            writer.AppendLine(BuildXL.Cache.BasicFilesystem.BasicFilesystemCache.END_TOKEN);

            writer.AppendLine(BasicFilesystemCache.CAS_TOKEN);
            for (int i = SHARD_COUNT; i < SHARD_COUNT * 2; i++)
            {
                writer.AppendLine(shardRoots[i]);
            }

            writer.AppendLine(BuildXL.Cache.BasicFilesystem.BasicFilesystemCache.END_TOKEN);

            return writer.ToString();
        }

        public override string NewCache(string cacheId, bool strictMetadataCasCoupling, bool authoritative = false)
        {
            string cacheDir = GenerateCacheFolderPath("C");

            using (StreamWriter writer = new StreamWriter(Path.Combine(cacheDir, "SHARDS")))
            {
                writer.Write(m_shardFileSource(cacheDir));
            }

            CacheId2cacheDir.Remove(cacheId);
            CacheId2cacheDir.Add(cacheId, cacheDir);

            return JsonConfig(cacheId, cacheDir, false, strictMetadataCasCoupling, authoritative);
        }

        [Fact]
        public async Task TestEmptyShardFile()
        {
            string baseCacheDir = null;
            m_shardFileSource = (cacheDir) =>
            {
                baseCacheDir = cacheDir;
                return null;
            };

            string cacheConfig = NewCache("EmptyShardFile", false);
            BasicFilesystemCache cache = (await InitializeCacheAsync(cacheConfig).SuccessAsync()) as BasicFilesystemCache;
            XAssert.IsNotNull(cache, "Failed to create cache for tests!");

            string casPath = Path.Combine(baseCacheDir, BasicFilesystemCache.CAS_HASH_TOKEN);

            foreach (string oneShardDir in cache.CasRoots)
            {
                XAssert.AreEqual(casPath, oneShardDir, "Cas Directory not as expected");
            }

            string wfpPath = Path.Combine(baseCacheDir, BasicFilesystemCache.WFP_TOKEN);

            foreach (string oneShardDir in cache.FingerprintRoots)
            {
                XAssert.AreEqual(wfpPath, oneShardDir, "Fingerprint directory not as expected");
            }
        }

        [Fact]
        public async Task TestTokenOnlyShardFile()
        {
            string baseCacheDir = null;
            m_shardFileSource = (cacheDir) =>
            {
                baseCacheDir = cacheDir;
                StringBuilder shardFile = new StringBuilder();
                shardFile.AppendLine(BasicFilesystemCache.WFP_TOKEN);
                shardFile.AppendLine(BasicFilesystemCache.END_TOKEN);
                shardFile.AppendLine(BasicFilesystemCache.CAS_TOKEN);
                shardFile.AppendLine(BasicFilesystemCache.END_TOKEN);
                return shardFile.ToString();
            };

            string cacheConfig = NewCache("TokenOnlyShardFile", false);
            BasicFilesystemCache cache = (await InitializeCacheAsync(cacheConfig).SuccessAsync()) as BasicFilesystemCache;
            XAssert.IsNotNull(cache, "Failed to create cache for tests!");

            string casPath = Path.Combine(baseCacheDir, BasicFilesystemCache.CAS_HASH_TOKEN);

            foreach (string oneShardDir in cache.CasRoots)
            {
                XAssert.AreEqual(casPath, oneShardDir, "Cas Directory not as expected");
            }

            string wfpPath = Path.Combine(baseCacheDir, BasicFilesystemCache.WFP_TOKEN);

            foreach (string oneShardDir in cache.FingerprintRoots)
            {
                XAssert.AreEqual(wfpPath, oneShardDir, "Fingerprint directory not as expected");
            }
        }

        [Fact]
        public async Task TestCasOnlyShardFile()
        {
            string baseCacheDir = null;
            m_shardFileSource = (cacheDir) =>
            {
                baseCacheDir = cacheDir;
                StringBuilder sb = new StringBuilder();
                sb.AppendLine(BasicFilesystemCache.WFP_TOKEN);
                sb.AppendLine(BasicFilesystemCache.END_TOKEN);
                sb.AppendLine(BasicFilesystemCache.CAS_TOKEN);
                sb.AppendLine(cacheDir + "CasDir");
                sb.AppendLine(BasicFilesystemCache.END_TOKEN);

                return sb.ToString();
            };

            string cacheConfig = NewCache("TestCasOnlyShardFile", false);
            BasicFilesystemCache cache = (await InitializeCacheAsync(cacheConfig).SuccessAsync()) as BasicFilesystemCache;
            XAssert.IsNotNull(cache, "Failed to create cache for tests!");

            string casPath = Path.Combine(baseCacheDir + "CasDir", BasicFilesystemCache.CAS_HASH_TOKEN);

            foreach (string oneShardDir in cache.CasRoots)
            {
                XAssert.AreEqual(casPath, oneShardDir, "Cas Directory not as expected");
            }

            string wfpPath = Path.Combine(baseCacheDir, BasicFilesystemCache.WFP_TOKEN);

            foreach (string oneShardDir in cache.FingerprintRoots)
            {
                XAssert.AreEqual(wfpPath, oneShardDir, "Fingerprint directory not as expected");
            }
        }

        [Fact]
        public async Task TestWfpOnlyShardFile()
        {
            string baseCacheDir = null;
            m_shardFileSource = (cacheDir) =>
            {
                baseCacheDir = cacheDir;
                StringBuilder sb = new StringBuilder();
                sb.AppendLine(BasicFilesystemCache.WFP_TOKEN);
                sb.AppendLine(cacheDir + "WfpDir");
                sb.AppendLine(BasicFilesystemCache.END_TOKEN);
                sb.AppendLine(BasicFilesystemCache.CAS_TOKEN);
                sb.AppendLine(BasicFilesystemCache.END_TOKEN);
                return sb.ToString();
            };

            string cacheConfig = NewCache("TestWfpOnlyShardFile", false);
            BasicFilesystemCache cache = (await InitializeCacheAsync(cacheConfig).SuccessAsync()) as BasicFilesystemCache;
            XAssert.IsNotNull(cache, "Failed to create cache for tests!");

            string casPath = Path.Combine(baseCacheDir, BasicFilesystemCache.CAS_HASH_TOKEN);

            foreach (string oneShardDir in cache.CasRoots)
            {
                XAssert.AreEqual(casPath, oneShardDir, "Cas Directory not as expected");
            }

            string wfpPath = Path.Combine(baseCacheDir + "WfpDir", BasicFilesystemCache.WFP_TOKEN);

            foreach (string oneShardDir in cache.FingerprintRoots)
            {
                XAssert.AreEqual(wfpPath, oneShardDir, "Fingerprint directory not as expected");
            }
        }

        [Fact]
        public async Task TestNonPowerOf2ShardFile()
        {
            string baseCacheDir = null;
            m_shardFileSource = (cacheDir) =>
            {
                baseCacheDir = cacheDir;
                StringBuilder sb = new StringBuilder();
                sb.AppendLine(BasicFilesystemCache.WFP_TOKEN);
                sb.AppendLine(cacheDir + "WfpDir");
                sb.AppendLine(cacheDir + "WfpDir2");
                sb.AppendLine(cacheDir + "WfpDir3");
                sb.AppendLine(BasicFilesystemCache.END_TOKEN);
                sb.AppendLine(BasicFilesystemCache.CAS_TOKEN);
                sb.AppendLine(BasicFilesystemCache.END_TOKEN);

                return sb.ToString();
            };

            string cacheConfig = NewCache("TestNonPowerOf2ShardFile", false);
            var failure = await InitializeCacheAsync(cacheConfig);
            XAssert.IsFalse(failure.Succeeded, "Cache creation did not fail");
        }

        [Fact]
        public async Task TestDistributionShardFile()
        {
            string baseCacheDir = null;
            m_shardFileSource = (cacheDir) =>
            {
                baseCacheDir = cacheDir;
                StringBuilder sb = new StringBuilder();
                sb.AppendLine(BasicFilesystemCache.WFP_TOKEN);
                for (int i = 0; i < 16; i++)
                {
                    sb.AppendLine(cacheDir + "WFP" + i);
                }

                sb.AppendLine(BasicFilesystemCache.END_TOKEN);

                sb.AppendLine(BasicFilesystemCache.CAS_TOKEN);
                sb.AppendLine(BasicFilesystemCache.END_TOKEN);

                return sb.ToString();
            };

            string cacheConfig = NewCache("TestDistributionShardFile", false);
            BasicFilesystemCache cache = (await InitializeCacheAsync(cacheConfig).SuccessAsync()) as BasicFilesystemCache;
            XAssert.IsNotNull(cache, "Failed to create cache for tests!");

            // We should have 16 blocks of 16 shards.
            Dictionary<string, int> shards = new Dictionary<string, int>();
            foreach (string shard in cache.FingerprintRoots)
            {
                if (!shards.ContainsKey(shard))
                {
                    shards.Add(shard, 0);
                }

                shards[shard]++;
            }

            XAssert.AreEqual(16, shards.Count, "Did not have the correct number of shards");
            foreach (string shard in shards.Keys)
            {
                XAssert.AreEqual(256, shards[shard], "Shard {0} did not have the correct number of entries.", shard);
            }
        }
    }
}
