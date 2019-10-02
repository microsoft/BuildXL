// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BuildXL.Xldb;
using BuildXL.Xldb.Proto;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;


namespace Xldb.Analyzer
{
    /// <summary>
    /// Xldb Analyzers that use the rocksDb instance and the Xldb API to analyze BXL logs
    /// </summary>
    public class Program
    {
        private const string s_eventStatsAnalyzer = "dbstats";
        private const string s_dumpPipAnalyzer = "dumppip";
        private const double m_thousandDivisor = 1000.0; // Bytes to Kilobytes

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
                        return p.AnalyzeDbStats();
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
            // Check that the pipHash string starts with PIP
            if (!pipHash.ToUpperInvariant().StartsWith("PIP"))
            {
                parsedHash = -1;
                return false;
            }

            var hexedHash = pipHash.ToUpperInvariant().Substring(3);
            return long.TryParse(hexedHash, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsedHash);
        }

        /// <summary>
        /// Analyzes the event stats from the Xldb instance
        /// </summary>
        public int AnalyzeDbStats()
        {
            if (m_commandLineOptions.ContainsKey("/h"))
            {
                Console.WriteLine("\nDB Stats Analyzer");
                Console.WriteLine("Dumps stats on the aggregate size and count of different items stored in the Xldb instance.");
                Console.WriteLine("/i: \t Required \t The directory to read the RocksDB database from");
                Console.WriteLine("/o: \t Required \t The file where to write the results");
                return 1;
            }

            if (!m_commandLineOptions.TryGetValue("/o", out var outputFilePath))
            {
                Console.WriteLine("Output file required. Exiting analyzer ...");
                return 1;
            }

            m_commandLineOptions.TryGetValue("/i", out var inputRocksDbDir);

            using (var dataStore = new XldbDataStore(storeDirectory: inputRocksDbDir))
            using (var outputStream = File.OpenWrite(outputFilePath))
            using (var writer = new StreamWriter(outputStream))
            {
                var maxLength = Enum.GetValues(typeof(DBStoredTypes)).Cast<DBStoredTypes>().Select(e => e.ToString().Length).Max();
                var storageMetadata = new List<DBStorageStatsValue>();
                ulong totalCount = 0;
                ulong totalPayload = 0;

                writer.WriteLine("Storage stats (uncompressed) of the DB.\n\n");

                foreach (DBStoredTypes storageType in Enum.GetValues(typeof(DBStoredTypes)))
                {
                    var dbStorageStatValue = dataStore.GetDBStatsInfoByStorageType(storageType);

                    if (dbStorageStatValue != null)
                    {
                        totalCount += dbStorageStatValue.Count;
                        totalPayload += dbStorageStatValue.Size;
                        storageMetadata.Add(dbStorageStatValue);
                    }
                    else
                    {
                        storageMetadata.Add(new DBStorageStatsValue());
                    }
                }

                // 12 digits of padding for count/payload
                var countPadding = 12; 
                // 8 digits of percentage paddin
                var percentagePadding = 8;

                for (var i = 0; i < storageMetadata.Count; i++)
                {
                    var currDbStorageStatValue = storageMetadata[i];

                    writer.WriteLine(
                    "{0}: Count = {1} K ( {2} %),   Payload = {3} KB ( {4} %)",
                    ((DBStoredTypes)i).ToString().PadRight(maxLength, ' '),
                    (currDbStorageStatValue.Count/ m_thousandDivisor).ToString("#.####").PadLeft(countPadding, ' '),
                    (currDbStorageStatValue.Count / (double)totalCount * 100).ToString("#.####").PadLeft(percentagePadding, ' '),
                    (currDbStorageStatValue.Size / m_thousandDivisor).ToString("#.####").PadLeft(countPadding, ' '),
                    (currDbStorageStatValue.Size / (double)totalPayload * 100).ToString("#.####").PadLeft(percentagePadding, ' '));
                }

                writer.WriteLine($"\n\nTotal uncompressed storage size is: {totalPayload/m_thousandDivisor} KB");
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
                Console.WriteLine($"Invalid PipId: {pipHash}. PipId must be a semistable hash that starts with Pip i.e.: PipC623BCE303738C69. Exiting analyzer ...");
                return 1;
            }

            if (!m_commandLineOptions.TryGetValue("/o", out var outputFilePath))
            {
                Console.WriteLine("Output file required. Exiting analyzer ...");
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
                writer.WriteLine("Pip Information: \n" + JsonConvert.SerializeObject(pip, Formatting.Indented));

                uint pipId = castedPip.GraphInfo.PipId;

                writer.WriteLine("Pip Execution Performance Information:\n");
                foreach (var i in dataStore.GetPipExecutionPerformanceEventByKey(pipId))
                {
                    writer.WriteLine(JToken.Parse(JsonConvert.SerializeObject(i, Formatting.Indented)));
                }

                writer.WriteLine("Pip Execution Step Performance Information:\n");
                foreach (var i in dataStore.GetPipExecutionStepPerformanceEventByKey(pipId))
                {
                    writer.WriteLine(JToken.Parse(JsonConvert.SerializeObject(i, Formatting.Indented)));
                }

                writer.WriteLine("Process Execution Monitoring Information:\n");
                foreach (var i in dataStore.GetProcessExecutionMonitoringReportedEventByKey(pipId))
                {
                    writer.WriteLine(JToken.Parse(JsonConvert.SerializeObject(i, Formatting.Indented)));
                }

                writer.WriteLine("Directory Membership Hashed Information:\n");
                foreach (var i in dataStore.GetDirectoryMembershipHashedEventByKey(pipId))
                {
                    writer.WriteLine(JToken.Parse(JsonConvert.SerializeObject(i, Formatting.Indented)));
                }

                writer.WriteLine("Dependency Violation Reported Event:\n");
                var depViolationEvents = dataStore.GetDependencyViolationEventByKey(pipId);

                foreach (var ev in depViolationEvents)
                {
                    writer.WriteLine(JsonConvert.SerializeObject(ev, Formatting.Indented));
                }

                if (pipType == PipType.Process)
                {
                    writer.WriteLine("Getting directory output information for Process Pip\n");

                    foreach (var output in dataStore.GetPipExecutionDirectoryOutputEventByKey(pipId))
                    {
                        foreach (var file in output.FileArtifactArray)
                        {
                            writer.WriteLine(JsonConvert.SerializeObject(file, Formatting.Indented));
                        }
                    }

                    writer.WriteLine("Geting directory dependency information for Process Pip\n");

                    var pipGraph = dataStore.GetPipGraphMetaData();
                    var sealDirectoryAndProducersDict = new Dictionary<DirectoryArtifact, uint>();

                    foreach (var kvp in pipGraph.AllSealDirectoriesAndProducers)
                    {
                        sealDirectoryAndProducersDict.Add(kvp.Artifact, kvp.PipId);
                    }

                    var directories = new Stack<(DirectoryArtifact artifact, string path)>(
                        ((ProcessPip)castedPip).DirectoryDependencies
                            .Select(d => (artifact: d, path: d.Path.Value))
                            .OrderByDescending(tupple => tupple.path));

                    while (directories.Count > 0)
                    {
                        var directory = directories.Pop();
                        writer.WriteLine(JsonConvert.SerializeObject(directory, Formatting.Indented));

                        if (sealDirectoryAndProducersDict.TryGetValue(directory.artifact, out var currentPipId))
                        {
                            var currPip = dataStore.GetPipByPipId(currentPipId, out var currPipType);

                            if (currPipType == PipType.SealDirectory)
                            {
                                foreach (var nestedDirectory in ((SealDirectory)currPip).ComposedDirectories.Select(d => (artifact: d, path: d.Path.Value)).OrderByDescending(tuple => tuple.path))
                                {
                                    directories.Push((nestedDirectory.artifact, nestedDirectory.path));
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