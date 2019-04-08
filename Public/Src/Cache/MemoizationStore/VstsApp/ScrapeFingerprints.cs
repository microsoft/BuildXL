// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using CLAP;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;

namespace BuildXL.Cache.MemoizationStore.VstsApp
{
    internal sealed partial class Application
    {
        /// <summary>
        ///     Scrape all strong fingerprints from a log.
        /// </summary>
        // ReSharper disable once UnusedMember.Global
        [Verb(Aliases = "scrape", Description = "Scrape a cache log file for all strong fingerprints")]
        internal void ScrapeFingerprints(
            [Required, Description("Log file path")] string log,
            [Required, Description("Output path to dump the strong fingerprints")] string output)
        {
            Initialize();

            Stopwatch stopwatch = Stopwatch.StartNew();
            int count = 0;
            AbsolutePath logPath = new AbsolutePath(log);
            AbsolutePath outputPath = new AbsolutePath(output);

            if (!_fileSystem.FileExists(logPath))
            {
                throw new ArgumentException($"Log file {log} does not exist.", nameof(log));
            }

            using (var logStream = _fileSystem.OpenReadOnlySafeAsync(logPath, FileShare.Read | FileShare.Delete).Result)
            using (StreamReader reader = new StreamReader(logStream))
            using (var outputStream = _fileSystem.OpenSafeAsync(outputPath, FileAccess.ReadWrite, FileMode.Create, FileShare.Read | FileShare.Delete).Result)
            using (StreamWriter writer = new StreamWriter(outputStream))
            {
                foreach (StrongFingerprint strongFingerprint in EnumerateUniqueStrongFingerprints(reader))
                {
                    writer.WriteLine(strongFingerprint);
                    count++;
                }
            }

            stopwatch.Stop();
            _logger.Always($"Found {count} unique strong fingerprints in {stopwatch.ElapsedMilliseconds / 1000} seconds.");
        }

        private IEnumerable<StrongFingerprint> EnumerateUniqueStrongFingerprints(StreamReader reader)
        {
            ConcurrentDictionary<StrongFingerprint, int> uniqueStrongFingerprints = new ConcurrentDictionary<StrongFingerprint, int>();

            // Look for pattern: GetContentHashList(WeakFingerprint=[8033C0365DE491734D48A85A5709099B9B6A02D2],Selector=[ContentHash=[VSO0:000000000000000000000000000000000000000000000000000000000000000000], Output=[D697E34F2B7242DE55AFA03220E72DE2ED1D7DE0]]) start
            // Hits on this will also hit AddOrGetContentHashList:
            // AddOrGetContentHashList(WeakFingerprint=[AF00A265EB9B856129B5CBB41D5B7FE15D0CBC26],Selector=[ContentHash=[VSO0:000000000000000000000000000000000000000000000000000000000000000000], Output=[56552E044A46FA8AB1AC8660C62221A4BE8497C4]]) start
            const string strongFingerprintPattern = @"WeakFingerprint=\[(?<weakFingerprint>\w*)\],Selector=\[ContentHash=\[(?<selectorHash>[^\]]*)\], Output=\[(?<selectorOutput>\w*)\]\]";
            Regex sfpRegex = new Regex(strongFingerprintPattern, RegexOptions.IgnoreCase);

            string currentLine = reader.ReadLine();
            while (currentLine != null)
            {
                Match match = sfpRegex.Match(currentLine);
                if (match.Success && match.Groups["weakFingerprint"].Success && match.Groups["selectorHash"].Success && match.Groups["selectorOutput"].Success)
                {
                    string weakFingerprintString = match.Groups["weakFingerprint"].Value;
                    string selectorHashString = match.Groups["selectorHash"].Value;
                    string selectorOutputString = match.Groups["selectorOutput"].Value;

                    Fingerprint weakFingerprint;
                    ContentHash selectorHash;
                    byte[] selectorOutput = null;
                    if (Fingerprint.TryParse(weakFingerprintString, out weakFingerprint) && ContentHash.TryParse(selectorHashString, out selectorHash))
                    {
                        if (!string.IsNullOrEmpty(selectorOutputString))
                        {
                            selectorOutput = HexUtilities.HexToBytes(selectorOutputString);
                        }

                        StrongFingerprint strongFingerprint = new StrongFingerprint(
                            weakFingerprint, new Selector(selectorHash, selectorOutput));
                        if (uniqueStrongFingerprints.TryAdd(strongFingerprint, 0))
                        {
                            yield return strongFingerprint;
                        }
                    }
                }

                currentLine = reader.ReadLine();
            }
        }
    }
}
