// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BuildXL.Xldb;
using BuildXL.Xldb.Proto;

namespace Xldb.Analyzer
{
    /// <summary>
    /// Xldb Analyzers that use the rocksDb instanceand the Xldb API to analyze BXL logs
    /// </summary>
    public class Program
    {
        private const string s_eventStatsAnalyzer = "eventstats";
        private const string s_dumpPipAnalyzer = "dumppip";

        private Dictionary<string, string> m_commandLineOptions = new Dictionary<string, string>();

        public static int Main(string[] args)
        {
            var p = new Program();

            if (!p.ProcessArguments(args))
            {
                Console.WriteLine("Invalid arguments passed in, exiting analyzer ...");
                return 1;
            }

            if (p.m_commandLineOptions.TryGetValue("/m", out var mode))
            {
                switch (mode)
                {
                    case s_eventStatsAnalyzer:
                       return p.AnalyzeEventStats();
                    case s_dumpPipAnalyzer:
                        return p.AnalyzeDumpPip();
                    default:
                        Console.WriteLine("Invalid mode passed in.");
                        break;
                }
            }
            else
            {
                Console.WriteLine("The mode flag, /m:, is required.");
            }

            Console.Write($"Valid modes are: /m:{s_eventStatsAnalyzer}, /m:{s_dumpPipAnalyzer}");

            return 1;
        }

        /// <summary>
        /// Processes the arguments, checking for the input db path param and the help param
        /// </summary>
        public bool ProcessArguments(string[] args)
        {
            try
            {
                m_commandLineOptions = args.Select(s => s.Split(new[] { ':' }, 2)).ToDictionary(s => s[0].ToLowerInvariant(), s => s[1].ToLowerInvariant());
            }
            catch (IndexOutOfRangeException)
            {
                Console.WriteLine("One or more options passed in do not have a key/value. Ie /key:value.");
                return false;
            }

            if (!m_commandLineOptions.ContainsKey("/i"))
            {
                Console.WriteLine("Input db path required.");
                return false;
            }

            if (m_commandLineOptions.ContainsKey("/h"))
            {
                Console.WriteLine("Welcome to the xldb analyzer where you can use a rocksdb instance and the Xldb api to analyze BXL logs.");
                Console.WriteLine($"The current valid analyzers are: /m:{s_eventStatsAnalyzer}, /m:{s_dumpPipAnalyzer}");
            }

            return true;
        }

        /// <summary>
        /// Parses the long representation of the semistable hash from the string passed in (eg. PipC623BCE303738C69 -> 14277462926896108000)
        /// </summary>
        public bool ParseSemistableHash(string pipHash, out long parsedHash)
        {
            var adjustedOption = pipHash.ToUpper().Replace("PIP", "");
            return (long.TryParse(adjustedOption, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsedHash) || parsedHash == 0);
        }

        /// <summary>
        /// Analyzes the event stats from the Xldb instance
        /// </summary>
        public int AnalyzeEventStats()
        {
            if (m_commandLineOptions.ContainsKey("/h"))
            {
                Console.WriteLine("\nEvent Stats Analyzer");
                Console.WriteLine("Generates stats on the aggregate size and count of execution log events, but uses the RocksDB database as the source of truth");
                Console.WriteLine("/i: \t Required \t The directory to read the RocksDB database from");
                Console.WriteLine("/o: \t Required \t The file where to write the results");
                return 1;
            }

            if (!m_commandLineOptions.TryGetValue("/o", out var outputFilePath))
            {
                Console.WriteLine("Output directory required. Exiting analyzer ...");
                return 1;
            }

            m_commandLineOptions.TryGetValue("/i", out var inputRocksDbDir);

            using (var dataStore = new XldbDataStore(storeDirectory: inputRocksDbDir))
            using (var outputStream = File.OpenWrite(outputFilePath))
            using (var writer = new StreamWriter(outputStream))
            {
                var workerToEventDict = new Dictionary<uint, Dictionary<ExecutionEventId, (int, int)>>();
                foreach (ExecutionEventId eventId in Enum.GetValues(typeof(ExecutionEventId)))
                {
                    var eventCount = dataStore.GetCountByEvent(eventId);

                    if (eventCount != null)
                    {
                        foreach (var workerCount in eventCount.WorkerToCountMap)
                        {
                            if (workerToEventDict.TryGetValue(workerCount.Key, out var eventDict))
                            {
                                eventDict[eventId] = (workerCount.Value, 0);
                            }
                            else
                            {
                                var dict = new Dictionary<ExecutionEventId, (int, int)>();
                                dict.Add(eventId, (workerCount.Value, 0));
                                workerToEventDict.Add(workerCount.Key, dict);
                            }
                        }
                        foreach (var payloadSize in eventCount.WorkerToPayloadMap)
                        {
                            workerToEventDict.TryGetValue(payloadSize.Key, out var eventDict);
                            eventDict.TryGetValue(eventId, out var tup);
                            eventDict[eventId] = (tup.Item1, payloadSize.Value);
                        }
                    }
                }

                foreach (var workerDict in workerToEventDict)
                {
                    writer.WriteLine("Worker {0}", workerDict.Key);
                    var maxLength = Enum.GetValues(typeof(ExecutionEventId)).Cast<ExecutionEventId>().Select(e => e.ToString().Length).Max();
                    foreach (var eventStats in workerDict.Value)
                    {
                        writer.WriteLine(
                        "{0}: {1} Count = {2}",
                        eventStats.Key.ToString().PadRight(maxLength, ' '),
                        eventStats.Value.Item2.ToString(CultureInfo.InvariantCulture).PadLeft(12, ' '),
                        eventStats.Value.Item1.ToString(CultureInfo.InvariantCulture).PadLeft(12, ' '));
                    }
                    writer.WriteLine();
                }
            }

            return 0;
        }
          
        /// <summary>
        /// Dumps the information related to a pip from the Xldb instance
        /// </summary>
        public int AnalyzeDumpPip()
        {
            if (m_commandLineOptions.ContainsKey("/h"))
            {
                Console.WriteLine("\nDump Pip Xldb Analyzer");
                Console.WriteLine("Creates a file containing information about the requested pip, using the RocksDB database as the source");
                Console.WriteLine("/i: \t Required \t The directory to read the RocksDB database from");
                Console.WriteLine("/o: \t Required \t The file where to write the results");
                Console.WriteLine("/p: \t Required \t The formatted semistable hash of a pip to dump (must start with 'Pip', e.g., 'PipC623BCE303738C69')");
                return 1;
            }

            if (!m_commandLineOptions.TryGetValue("/p", out var pipHash))
            {
                Console.WriteLine("Pip Semistable Hash is required. Exiting analyzer ...");
                return 1;
            }

            if (!ParseSemistableHash(pipHash, out var parsedSemiStableHash))
            {
                Console.WriteLine($"Invalid pip: {pipHash}. Id must be a semistable hash that starts with Pip i.e.: PipC623BCE303738C69. Exiting analyzer ...");
                return 1;
            }

            if (!m_commandLineOptions.TryGetValue("/o", out var outputFilePath))
            {
                Console.WriteLine("Output directory required. Exiting analyzer ...");
                return 1;
            }

            m_commandLineOptions.TryGetValue("/i", out var inputRocksDbDir);

            using (var dataStore = new XldbDataStore(storeDirectory: inputRocksDbDir))
            using (var outputStream = File.OpenWrite(outputFilePath))
            using (var writer = new StreamWriter(outputStream))
            {
                var pip = dataStore.GetPipBySemiStableHash(parsedSemiStableHash, out var pipType);

                if (pip == null)
                {
                    Console.WriteLine($"Pip with the SemiStableHash {parsedSemiStableHash} was not found. Exiting Analyzer ...");
                    return 1;
                }
                Console.WriteLine($"Pip with the SemiStableHash {parsedSemiStableHash} was found. Logging to output file ...");

                dynamic castedPip = null;

                switch (pipType)
                {
                    case PipType.CopyFile:
                        castedPip = (CopyFile)pip;
                        break;
                    case PipType.SealDirectory:
                        castedPip = (SealDirectory)pip;
                        break;
                    case PipType.WriteFile:
                        castedPip = (WriteFile)pip;
                        break;
                    case PipType.Process:
                        castedPip = (ProcessPip)pip;
                        break;
                    case PipType.Ipc:
                        castedPip = (IpcPip)pip;
                        break;
                }

                writer.WriteLine($"PipType: {pipType.ToString()}");
                writer.WriteLine("Pip Information: \n" + pip.ToString());

                uint pipId = castedPip.GraphInfo.PipId;

                writer.WriteLine("Bxl Invocation Information:\n");
                dataStore.GetBXLInvocationEvents().ToList().ForEach(ev => writer.WriteLine(ev.ToString()));
                writer.WriteLine("Pip Execution Performance Information:\n");
                dataStore.GetPipExecutionPerformanceEventByKey(pipId).ToList().ForEach(i => writer.WriteLine(i));
                writer.WriteLine("Pip Execution Step Performance Information:\n");
                dataStore.GetPipExecutionStepPerformanceEventByKey(pipId).ToList().ForEach(i => writer.WriteLine(i));
                writer.WriteLine("Process Execution Monitoring Information:\n");
                dataStore.GetProcessExecutionMonitoringReportedEventByKey(pipId).ToList().ForEach(i => writer.WriteLine(i));
                writer.WriteLine("Process Fingerprint Computation Information:\n");
                dataStore.GetProcessFingerprintComputationEventByKey(pipId).ToList().ForEach(i => writer.WriteLine(i));
                writer.WriteLine("Directory Membership Hashted Information:\n");
                dataStore.GetDirectoryMembershipHashedEventByKey(pipId).ToList().ForEach(i => writer.WriteLine(i));

                writer.WriteLine("Dependency Violation Reported Event:\n");
                var depViolatedEvents = dataStore.GetDependencyViolationReportedEvents();

                foreach (var ev in depViolatedEvents)
                {
                    if (ev.ViolatorPipID == castedPip.GraphInfo.PipId || ev.RelatedPipID == castedPip.GraphInfo.PipId)
                    {
                        writer.WriteLine(ev.ToString());
                    }
                }

                if (pipType == PipType.Process)
                {
                    writer.WriteLine("Getting directory output information for Process Pip");
                    var pipExecutionDirEvents = dataStore.GetPipExecutionDirectoryOutputsEvents();
                    foreach (var ev in pipExecutionDirEvents)
                    {
                        if (castedPip.DirectoryOutputs.Contains(ev.DirectoryArtifact))
                        {
                            ev.FileArtifactArray.ToList().ForEach(file => writer.WriteLine(file.ToString()));
                        }
                    }

                    writer.WriteLine("Geting directory dependency information for Process Pip");

                    var pipGraph = dataStore.GetPipGraphMetaData();
                    var directories = new Stack<(DirectoryArtifact artifact, string path)>(
                        ((ProcessPip)castedPip).DirectoryDependencies
                            .Select(d => (artifact: d, path: d.Path.Value))
                            .OrderByDescending(tupple => tupple.path));

                    while (directories.Count > 0)
                    {
                        var directory = directories.Pop();
                        writer.WriteLine(directory.ToString());

                        foreach (var kvp in pipGraph.AllSealDirectoriesAndProducers)
                        {
                            if (kvp.Artifact == directory.artifact)
                            {
                                var currPipId = kvp.PipId;
                                var currPip = dataStore.GetPipByPipId(currPipId, out var currPipType);

                                if (currPipType == PipType.SealDirectory)
                                {
                                    foreach (var nestedDirectory in ((SealDirectory)currPip).ComposedDirectories.Select(d => (artifact: d, path: d.Path.Value)).OrderByDescending(tupple => tupple.path))
                                    {
                                        directories.Push((nestedDirectory.artifact, nestedDirectory.path));
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return 0;
        }
    }
}