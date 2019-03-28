// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using CLAP;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;

// ReSharper disable once UnusedMember.Global
namespace BuildXL.Cache.MemoizationStore.App
{
    internal sealed partial class Application
    {
        /// <summary>
        ///     List content hash lists verb.
        /// </summary>
        [Verb(Aliases = "gchl", Description = "Get all content hash lists")]
        public void GetContentHashLists
            (
            [Required, Description("Cache root directory")] string root,
            [DefaultValue(16), Description("Max number of concurrent requests")] int maxDegreeOfParallelism,
            [DefaultValue(false), Description("If true, don't show results")] bool quiet,
            [DefaultValue(0u), Description("Delay to insert before store shutdown")] uint preShutdownDelaySeconds,
            [DefaultValue(false), Description("If true, show the ContentHashLists alongside the StringFingerprints.")] bool printContentHashLists
            )
        {
            var stopwatch = Stopwatch.StartNew();

            // ReSharper disable once ArgumentsStyleLiteral
            // ReSharper disable once ArgumentsStyleAnonymousFunction
            RunSQLiteStoreSession(new AbsolutePath(root), lruEnabled: false, funcAsync: async (context, store, session) =>
            {
                var printerBlock = new ActionBlock<StructResult<StrongFingerprint>>(
                    async strongFingerprintResult =>
                    {
                        var contentHashListString = string.Empty;
                        if (strongFingerprintResult.Succeeded && printContentHashLists)
                        {
                            var getResult = await session.GetContentHashListAsync(
                                context, strongFingerprintResult.Data, _token, UrgencyHint.Nominal).ConfigureAwait(false);
                            contentHashListString = getResult.Succeeded
                                ? $" => ContentHashList=[{getResult.ContentHashListWithDeterminism}]"
                                : $" => ContentHashList=[{getResult}]";
                        }

                        if (!quiet)
                        {
                            var strongFingerprintString = strongFingerprintResult.Succeeded
                                ? strongFingerprintResult.Data.ToString()
                                : strongFingerprintResult.ToString();
                            _logger.Always($"StrongFingerprint=[{strongFingerprintString}]{contentHashListString}");
                        }
                    },
                    new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism });

                var enumerationStopwatch = Stopwatch.StartNew();
                using (var strongFingerprints = store.EnumerateStrongFingerprints(context).GetEnumerator())
                {
                    int strongFingerprintCount = 0;
                    while (await strongFingerprints.MoveNext(_token))
                    {
                        strongFingerprintCount++;
                        printerBlock.Post(strongFingerprints.Current);
                    }

                    enumerationStopwatch.Stop();

                    var getterStopwatch = Stopwatch.StartNew();
                    printerBlock.Complete();
                    await printerBlock.Completion;
                    getterStopwatch.Stop();

                    _logger.Always($"{strongFingerprintCount} records");
                    _logger.Always($"Enumerate time: {(int)enumerationStopwatch.Elapsed.TotalMilliseconds}ms");
                    if (printContentHashLists)
                    {
                        _logger.Always($"Get time: {(int)getterStopwatch.Elapsed.TotalMilliseconds}ms");
                    }

                    var statsResult = await store.GetStatsAsync(context).ConfigureAwait(false);
                    statsResult.CounterSet.LogOrderedNameValuePairs(s => _tracer.Debug(context, s));

                    // Useful for debugging LRU behavior.
                    if (preShutdownDelaySeconds > 0)
                    {
                        _logger.Always($"Delaying {preShutdownDelaySeconds} seconds before store shutdown");
                        await Task.Delay(TimeSpan.FromSeconds(preShutdownDelaySeconds), _token);
                    }
                }
            });

            _logger.Always($"Total time: {(int)stopwatch.Elapsed.TotalMilliseconds}ms");
        }
    }
}
