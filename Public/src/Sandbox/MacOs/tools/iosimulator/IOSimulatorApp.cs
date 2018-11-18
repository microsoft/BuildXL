// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Diagnostics;
using System.Xml;
using System.Runtime.InteropServices;

namespace IOSimulator
{
    class IOSimulatorApp
    {
        private static int rate = 0;
        private static int duration = 0;
        private static int workerCount = 2;
        private static int samplePercentage = 5;

        private static ConcurrentDictionary<int, (int, int)> stats = new ConcurrentDictionary<int, (int, int)>();

        public static bool Verbose = false;
        public static bool Logging = false;
        public static string ObserverPattern = "*";

        public static string InputHashesLogPath = @"inputHashes.log";
        public static string OutputHashesLogPath = @"outputHashes.log";

        // Get the application settings
        private static bool ReadSettings()
        {
            try
            {
                var appSettings = ConfigurationManager.AppSettings;
                if (appSettings.Count > 0)
                {
                    foreach (string key in appSettings.AllKeys)
                    {
                        switch (key)
                        {
                            case "duration":
                            {
                                duration = Int32.Parse(appSettings[key] ?? "0");
                                break;
                            }
                            case "rate":
                            {
                                // Fallback to 200 hashed files / second
                                rate = Int32.Parse(appSettings[key] ?? "200");
                                break;
                            }
                            case "worker_count":
                            {
                                workerCount = Int32.Parse(appSettings[key] ?? "2");
                                break;
                            }
                            case "observer_pattern":
                            {
                                ObserverPattern = appSettings[key] ?? "*";
                                break;
                            }
                            case "sample_percentage":
                            {
                                samplePercentage = Int32.Parse(appSettings[key] ?? "5");
                                break;
                            }
                            case "verbose":
                            {
                                Verbose = Convert.ToBoolean(appSettings[key] ?? "false");
                                break;
                            }
                            case "logging":
                            {
                                Logging = Convert.ToBoolean(appSettings[key] ?? "false");
                                break;
                            }
                            default:
                                break;
                        }
                    }

                    if (duration > 0)
                    {
                        return true;
                    }

                    Console.WriteLine(@"No valid configuration settings found, exiting!");
                }
            }
            catch (ConfigurationErrorsException ex)
            {
                Console.WriteLine(ex.ToString());
            }

            return false;
        }

        private enum SectionName
        {
            None,
            Input,
            Output
        }

        private static (int, int, int, int) GetInputAndOutputEntriesCount(string inputFilePath)
        {
            try
            {
                // Used to partition the input file
                int startLineInputs = 0, endLineInputs = 0, startLineOutputs = 0, currentLine = 1;

                using (StreamReader sr = new StreamReader(inputFilePath))
                {
                    SectionName currentSection = SectionName.None;
                    string line;

                    while ((line = sr.ReadLine()) != null)
                    {
                        // Switch the lists to put inputs and outputs in the correct places
                        currentSection = line == "[Input]" ? SectionName.Input : line == "[Output]" ? SectionName.Output : currentSection != SectionName.None ? currentSection : SectionName.None;

                        if (currentSection == SectionName.None)
                        {
                            Console.WriteLine("Please add [Input] and [Output] sections to the input file.");
                            break;
                        }

                        if (line == "[Input]")
                        {
                            startLineInputs = currentLine + 1;
                        }
                        else if (line == "[Output]")
                        {
                            endLineInputs = currentLine - 1;
                            startLineOutputs = currentLine + 1;
                        }

                        currentLine++;
                    }
                }

                return (startLineInputs, endLineInputs, startLineOutputs, currentLine - 1);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }

            return (0, 0, 0, 0);
        }

        private static void benchmark(ref (int, int, int, int) inputOutputCounts, string inputFilePath)
        {
            Console.WriteLine("Running benchmark on {0}% of randomized input sources, please be patient...", samplePercentage);

            string configLocation = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "IOSimulator.dll.config");
            var numberOfInputs = Math.Max(inputOutputCounts.Item1, inputOutputCounts.Item2) - Math.Min(inputOutputCounts.Item1, inputOutputCounts.Item2);
            int filesHashed = 0;

            // Benchmark with the configured samplePrecentage of input files, this will be raw sequential read from one thread only
            Stopwatch benchmark = Stopwatch.StartNew();
            var samples = File.ReadLines(inputFilePath).Skip((inputOutputCounts.Item1 - 1)).Take(((numberOfInputs / 100) * samplePercentage));

            foreach (string inputFile in samples)
            {
                try
                {
                    if (!File.Exists(inputFile)) continue;
                    Hashing.HashFileWithPath(inputFile, out var hash, Verbose);
                }
                catch (Exception)
                {
                    // Just continue if file can't be hashed in the benchmark run
                    continue;
                }

                filesHashed++;
            }

            benchmark.Stop();

            double benchmarkLengthSeconds = ((double) benchmark.ElapsedMilliseconds) / 1000.0;
            double filesHashedPerSecond = ((double) filesHashed) / benchmarkLengthSeconds;
            double rateWithPrecision = Math.Ceiling(filesHashedPerSecond);
            long rate = (long) rateWithPrecision;

            Console.WriteLine("---------------------------------------------------");
            Console.WriteLine("Hashed:\t{0} files", filesHashed);
            Console.WriteLine("Rate:\t{0} files hashed / second with single worker", rate);
            Console.WriteLine("Rate:\t{0} for {1} worker(s)", rate * workerCount, workerCount);
            Console.WriteLine("---------------------------------------------------");
            Console.Write("Adjusting your {0} please wait... ", configLocation);


            // Update the rate key in the app settings automatically
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.Load(configLocation);

                var ie = doc.SelectNodes("/configuration/appSettings/add").GetEnumerator();

                while (ie.MoveNext())
                {
                    if ((ie.Current as XmlNode).Attributes["key"].Value =="rate")
                    {
                        var adjustedRate = rate * workerCount;
                        (ie.Current as XmlNode).Attributes["value"].Value = adjustedRate.ToString();
                    }
                }

                doc.Save(configLocation);
                Console.Write("Done!\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Adjusting IOSimulator config failed:\n{0}", ex.ToString());
            }
        }

        private static bool CheckInputOutputEntryCounts(string pathToInputFile, out (int, int, int, int) entryCounts)
        {
            entryCounts = GetInputAndOutputEntriesCount(pathToInputFile);
            if (entryCounts.Item1 == 0 || entryCounts.Item2 == 0 || entryCounts.Item3 == 0 || entryCounts.Item4 == 0)
            {
                Console.WriteLine("The input file contains no data, exiting!");
                return false;
            }

            return true;
        }

        static int Main(string[] args)
        {
            if (args.Length == 2)
            {
                if (args[0] == "--benchmark")
                {
                    if (!CheckInputOutputEntryCounts(args[1], out var inputOutputCounts))
                    {
                        return 1;
                    }

                    if (ReadSettings())
                    {
                        benchmark(ref inputOutputCounts, args[1]);
                        return 0;
                    }
                }

                Console.WriteLine("Usage: iosimulator [--benchmark] inputfile");
                return 1;
            }
            else if (args.Length == 1)
            {
                if (ReadSettings())
                {
                    if (rate <= 5 || rate >= 3500)
                    {
                        Console.WriteLine(@"Parsed rate is not within threshold (5 >= rate <= 3500) - please re-run the benchmark, exiting!");
                        return 1;
                    }

                    // inputOutputCounts.Item1 are input entry counts, Item2 are output folder counts
                    if (!CheckInputOutputEntryCounts(args[0], out var inputOutputCounts))
                    {
                        return 1;
                    }

                    var totalInputs = Math.Max(inputOutputCounts.Item1, inputOutputCounts.Item2) - Math.Min(inputOutputCounts.Item1, inputOutputCounts.Item2) + 1;
                    var totalOutputs = Math.Max(inputOutputCounts.Item3, inputOutputCounts.Item4) - Math.Min(inputOutputCounts.Item3, inputOutputCounts.Item4) + 1;

                    // Reset log files
                    if (File.Exists(InputHashesLogPath))
                    {
                        File.Delete(InputHashesLogPath);
                    }
                    if (File.Exists(OutputHashesLogPath))
                    {
                       File.Delete(OutputHashesLogPath);
                    }

                    // Make sure we have input and output hash logs in the case of verbose logging
                    File.Create(InputHashesLogPath).Close();
                    File.Create(OutputHashesLogPath).Close();

                    var tasks = new List<Task>();

                    // Workload
                    Action<object> InputHasher = (object context) =>
                    {
                        var workerContext = (ValueTuple<string, (int , int), int, CancellationToken>)context;
                        var inputFilePath = workerContext.Item1;

                        int startIndex = workerContext.Item2.Item1;
                        int count = workerContext.Item2.Item2;
                        CancellationToken ct = workerContext.Item4;

                        // The throttling heuristic is very simple, after some tests with an SSD and a beefy setup
                        // reading in and MD5 hashing a byte stream is on average ~550 files / sec or around 1.8 milliseconds
                        // per file. If we have a lot of input files e.g. 12.000.000 and the build duration is four hours or 14.400 seconds,
                        // we look at the coefficient of duration divided by number of files, in this case the hashing would need to take 0.0012
                        // seconds or 1.2 milliseconds per file. Because we said 1.8 milliseconds / file is the best we can do we don't throttle
                        // and just hash for the entire duration to create I/O load. Another case is we have 1 hour or 3600 seconds and 10000 files,
                        // so an average of 0.36 seconds or 360 milliseconds to hash per file. Now we need to slow down to not finish early,
                        // so we look at the difference between calculated hashing and expected hashing, 358 milliseconds and let the
                        // task sleep for this long in between every file hash. This would end up sleeping 0.358 seconds multiplied by 10000 files,
                        // so 3580 seconds or ~59:40 minutes. With the remaining 20 seconds multiplied by 550 files / seconds, we do the necessary
                        // 11000 file hashes.

                        // IMPORTANT: Running 'iosimulator --benchmark input.txt" does a sequential read and hash benchmark on samplePercentage of
                        //            input files and estimates the throughput rate - default is 200 files hashes / second. Users must run benchmark once!

                        int elementsProcessedTotal = 0;
                        int numberOfFailedFiles = 0;

                        double hashDurationPerFileMilliseconds = ((double) duration) / ((double) count) * 1000.0;
                        double durationForOneFileMilliseconds = 1000.0 / ((double) rate);

                        double sleepThreshold =
                            hashDurationPerFileMilliseconds > durationForOneFileMilliseconds ? (hashDurationPerFileMilliseconds - durationForOneFileMilliseconds) : 0.0;

                        startIndex = startIndex == 1 ? startIndex : ++startIndex;
                        var inputs = File.ReadLines(inputFilePath).Skip(startIndex).Take(count);
                        foreach (string inputFile in inputs)
                        {
                            // Cancel if requested
                            if (ct.IsCancellationRequested)
                            {
                                break;
                            }

                            try
                            {
                                if (!File.Exists(inputFile))
                                {
                                    numberOfFailedFiles++;
                                }
                                else
                                {
                                    if (Hashing.HashFileWithPath(inputFile, out var hash, Verbose))
                                    {
                                        elementsProcessedTotal++;

                                        if (Logging)
                                        {
                                            using (StreamWriter sw = File.AppendText(InputHashesLogPath))
                                            {
                                                sw.WriteLine("[{0} - Worker #{1}] Hashed: {2} with {3}", DateTime.Now, workerContext.Item3, inputFile, hash);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        numberOfFailedFiles++;
                                    }
                                }

                                // Add worker numbers to the statistics
                                stats.AddOrUpdate(workerContext.Item3, (elementsProcessedTotal, numberOfFailedFiles),
                                    (k, v) => (elementsProcessedTotal, numberOfFailedFiles));
                            }
                            catch (Exception ex)
                            {
                                if (Verbose) Console.WriteLine(ex.ToString());
                                continue;
                            }

                            Thread.Sleep((int)Math.Ceiling(sleepThreshold));
                        }
                    };


                    Stopwatch endToEndTime = Stopwatch.StartNew();

                    var tokenSource = new CancellationTokenSource();
                    var token = tokenSource.Token;

                    Console.WriteLine("Checking input data for correctness...");

                    // Output directory observers and hashers
                    List<OutputDirectortWatcher> outputDirectoryWatchers = new List<OutputDirectortWatcher>();

                    // Skip two entries (headers) and the total number of input paths to get the output paths to observe
                    var outputDirectories = File.ReadLines(args[0]).Skip((totalInputs + 2)).Take(totalOutputs).ToArray();

                    // Sort the output directories, look at the first two letter prefix and find the smallest common path
                    Array.Sort<string>(outputDirectories.ToArray());

                    int prefixLength = 3; // Check for '/xy' on Unix systems
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        prefixLength = 5; // Check for 'c:\\xy' on Windows systems
                    }

                    int lastIndex = 0;
                    string lastPrefix = outputDirectories[0].Substring(0, prefixLength);
                    var sanitizedOutputs = new List<string>();

                    for (int i = 0; i < outputDirectories.Length; ++i)
                    {
                        var prefix = outputDirectories[i].Substring(0, prefixLength);
                        // Make sure to at least get one directory
                        if (lastPrefix != prefix || ((i+1) == outputDirectories.Length && lastIndex == 0))
                        {
                            var subset = outputDirectories.SubArray(lastIndex, ((lastIndex == 0 && i == 0) ? outputDirectories.Length : i - lastIndex));
                            lastIndex = i;

                            string match = string.Join(Path.DirectorySeparatorChar.ToString(),
                                subset.Select(s => s.Split(Path.DirectorySeparatorChar).AsEnumerable()).Transpose()
                                    .TakeWhile(s => s.All(d => d == s.First())).Select(s => s.First()));

                            if (match.Length > 0) sanitizedOutputs.Add(match);
                        }

                        lastPrefix = prefix;
                    }

                    foreach (string outputPath in sanitizedOutputs)
                    {
                        try
                        {
                            // Don't observe files and deleted directories
                            FileAttributes attr = File.GetAttributes(outputPath);
                            if (!attr.HasFlag(FileAttributes.Directory)) continue;
                            if (!Directory.Exists(outputPath)) continue;

                            var watcher = new OutputDirectortWatcher(outputPath);
                            outputDirectoryWatchers.Add(watcher);
                            watcher.Start();
                        }
                        catch (Exception ex)
                        {
                            if (Verbose) Console.WriteLine(ex.ToString());
                        }
                    }

                    // Input hashers
                    int numberOfInputsPerWorker = totalInputs / workerCount;
                    for (int count = numberOfInputsPerWorker, index = 0, i = 0; i < workerCount; i++)
                    {
                        // Give the remainder of the work to the last worker
                        if (i == (workerCount - 1))
                        {
                            count += totalInputs % workerCount;
                        }

                        // Split work
                        tasks.Add(Task.Factory.StartNew(InputHasher, (args[0], ((index == 0 ? 1 : index), count), i, token), token));
                        index += numberOfInputsPerWorker;
                    }

                    try
                    {
                        Console.WriteLine("--------------------------------------------------------");
                        Console.WriteLine("NOTE: PLEASE RUN THE BENCHMARK ONCE TO ADJUST I/O RATES!");
                        Console.WriteLine("--------------------------------------------------------");
                        Console.WriteLine("Input files:\t\t{0}", totalInputs);
                        Console.WriteLine("Input workers:\t\t{0}", workerCount);
                        Console.WriteLine("Output directories:\t{0}", sanitizedOutputs.Count);
                        Console.WriteLine("Verbose:\t\t{0}", Verbose);
                        Console.WriteLine("Logging enabled:\t{0}", Logging);
                        Console.WriteLine("Hash rate:\t\t{0} files / second", rate);
                        Console.WriteLine("Duration:\t\t{0} seconds", duration);
                        Console.WriteLine("--------------------------------------------------------\n");

                        Console.CancelKeyPress += delegate(object sender, ConsoleCancelEventArgs e) {
                            tokenSource.Cancel();
                            tokenSource.Dispose();
                            Console.WriteLine("Execution canceled, writing out preliminary results...");
                            e.Cancel = true;
                        };

                        // Wait for all the tasks to finish or timeout after the configured max duration
                        // This also kill the output observers
                        tokenSource.CancelAfter(duration * 1000);
                        Task.WaitAll(tasks.ToArray(), duration * 1000, token);
                    }
                    catch (OperationCanceledException)
                    {
                        // This can happen if cancellation happens quicker than the wait threshold, ingore it
                    }
                    catch (AggregateException e)
                    {
                        foreach (Exception err in e.InnerExceptions)
                        {
                            if (Verbose) Console.WriteLine("\n---\n{0}", err.ToString());
                        }

                        return 1;
                    }
                    finally
                    {
                        tokenSource.Dispose();
                    }

                    printStats(ref endToEndTime, totalInputs, ref outputDirectoryWatchers);
                }
            }
            else
            {
                Console.WriteLine("Usage: iosimulator [--benchmark] inputfile");
                return 1;
            }

            return 0;
        }

        private static void printStats(ref Stopwatch endToEndTime, int totalInputs, ref List<OutputDirectortWatcher> outputDirectoryWatchers)
        {
            long endToEndMilliseconds = endToEndTime.ElapsedMilliseconds;
            endToEndTime.Stop();

            Console.WriteLine("Results:\n--------------------------------------------------------");

            for (int i=0; i < workerCount; i++)
            {
                ValueTuple<int, int> workerStats;
                if (stats.TryGetValue(i, out workerStats))
                {
                    Console.WriteLine("Worker #{0} hashed:\t{1} files", i, workerStats.Item1);
                }
            }

            var numberOfFilesHashed = stats.Sum(t => t.Value.Item1);
            Console.WriteLine("Inputs hashed:\t\t{0} ({1}%)", numberOfFilesHashed, (numberOfFilesHashed / (totalInputs / 100)));
            Console.WriteLine("Failed input files:\t{0}", stats.Sum(t => t.Value.Item2));
            Console.WriteLine("Outputs hashed:\t\t{0} (Creates / Updates)", outputDirectoryWatchers.Sum(w => w.filesHashed));
            Console.WriteLine("End to end time:\t{0} seconds", endToEndMilliseconds / 1000);
            Console.WriteLine("--------------------------------------------------------");
        }
    }
}
