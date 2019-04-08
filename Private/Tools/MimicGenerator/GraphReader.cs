// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Newtonsoft.Json;

namespace Tool.MimicGenerator
{
    /// <summary>
    /// Reads in a json build graph and writes out build specs and optionally dummy input files to be able to mimic
    /// a build.
    /// </summary>
    /// <remarks>
    /// This is currently working off the json graph. It'd be better to make it consume the SQL database instead so there
    /// are fewer separate components parsing the json directly. It would also benefit from additional data available in the database.
    ///
    /// This version has a number of shortcuts which make the build that runs less accurate:
    /// * All input and output file sizes are hardcoded 10 bytes
    /// * All process runtimes are hardcoded to some small amount
    /// * Using preducted intputs & outputs rather than actual
    /// * Launched processes are not consuming seal directory inputs
    /// * Copy file pips are not represented
    /// * Write file pips are not represented
    /// </remarks>
    public sealed class GraphReader
    {
        private readonly string m_graphPath;
        private readonly string m_observedInputsPath;

        private BuildGraph m_graph = new BuildGraph();

        /// <summary>
        /// Constructor
        /// </summary>
        public GraphReader(string graphPath, string observedInputsPath)
        {
            m_graphPath = graphPath;
            m_observedInputsPath = observedInputsPath;
        }

        public BuildGraph ReadGraph()
        {
            ReadJson();

            if (!string.IsNullOrWhiteSpace(m_observedInputsPath))
            {
                ReadObservedInputs();
            }

            return m_graph;
        }

        /// <summary>
        /// Parses the json file
        /// </summary>
        private void ReadJson()
        {
            Console.WriteLine("Parsing JSON graph");

            using (TextReader tr = new StreamReader(m_graphPath))
            {
                JsonTextReader reader = new JsonTextReader(tr);

                // Read into the file
                reader.Read();
                ExpectTokenType(reader, JsonToken.StartObject);
                reader.Read();
                ExpectTokenType(reader, JsonToken.PropertyName);

                while (reader.TokenType != JsonToken.EndObject)
                {
                    ExpectTokenType(reader, JsonToken.PropertyName);

                    if (CurrentValueMatches(reader, "description"))
                    {
                        reader.Read();
                        reader.Read();
                        ExpectTokenType(reader, JsonToken.PropertyName);
                    }
                    else if (CurrentValueMatches(reader, "dateUtc"))
                    {
                        reader.Read();
                        reader.Read();
                        ExpectTokenType(reader, JsonToken.PropertyName);
                    }
                    else if (CurrentValueMatches(reader, "version"))
                    {
                        reader.Read();
                        reader.Read();
                        ExpectTokenType(reader, JsonToken.PropertyName);
                    }
                    else if (CurrentValueMatches(reader, "artifacts"))
                    {
                        reader.Read();
                        ExpectTokenType(reader, JsonToken.StartArray);
                        ReadArtifactArray(reader);
                        ExpectTokenType(reader, JsonToken.PropertyName);
                    }
                    else if (CurrentValueMatches(reader, "filedetails"))
                    {
                        reader.Read();
                        ExpectTokenType(reader, JsonToken.StartArray);
                        ReadFileDetailsArray(reader);
                    }
                    else if (CurrentValueMatches(reader, "graph"))
                    {
                        reader.Read();
                        ExpectTokenType(reader, JsonToken.StartArray);
                        ReadGraphArray(reader);
                        ExpectTokenType(reader, JsonToken.PropertyName);
                    }
                    else if (CurrentValueMatches(reader, "execution"))
                    {
                        reader.Read();
                        ReadExecutionArray(reader);
                    }
                }
            }
        }

        private void ReadFileDetailsArray(JsonTextReader reader)
        {
            ExpectTokenType(reader, JsonToken.StartArray);
            reader.Read();
            while (reader.TokenType != JsonToken.EndArray)
            {
                ExpectTokenType(reader, JsonToken.StartObject);
                reader.Read();

                int fileId = -1;
                int length = -1;
                string hash = string.Empty;

                while (reader.TokenType != JsonToken.EndObject)
                {
                    if (CurrentValueMatches(reader, "file"))
                    {
                        fileId = reader.ReadAsInt32().Value;
                        reader.Read();
                    }
                    else if (CurrentValueMatches(reader, "length"))
                    {
                        length = reader.ReadAsInt32().Value;
                        reader.Read();
                    }
                    else if (CurrentValueMatches(reader, "hash"))
                    {
                        hash = reader.ReadAsString();
                        reader.Read();
                    }
                    else
                    {
                        reader.Read();
                    }
                }

                ExpectTokenType(reader, JsonToken.EndObject);
                reader.Read();

                if (fileId != -1)
                {
                    File file;
                    if (m_graph.Files.TryGetValue(fileId, out file))
                    {
                        file.SetUnscaledLength(length);
                        file.Hash = hash;
                        if (file.IsOutputFile)
                        {
                            m_graph.OutputFileStats.Add(length);
                        }
                        else
                        {
                            m_graph.SourceFileStats.Add(length);
                        }
                    }
                }
            }

            ExpectTokenType(reader, JsonToken.EndArray);
            reader.Read();
        }

        /// <summary>
        /// Reads the execution section
        /// </summary>
        private void ReadExecutionArray(JsonTextReader reader)
        {
            ExpectTokenType(reader, JsonToken.StartArray);
            reader.Read();
            while (reader.TokenType != JsonToken.EndArray)
            {
                Tuple<int, TimeSpan> pipAndRuntime = ReadExecutionItem(reader);
                ExpectTokenType(reader, JsonToken.EndObject);
                reader.Read();
                Pip p;
                if (m_graph.Pips.TryGetValue(pipAndRuntime.Item1, out p))
                {
                    Process process = p as Process;
                    if (process != null)
                    {
                        process.ProcessWallTimeMs = (int)pipAndRuntime.Item2.TotalMilliseconds;
                    }
                }
            }

            ExpectTokenType(reader, JsonToken.EndArray);
            reader.Read();
        }

        /// <summary>
        /// Reads an item in the execution array
        /// </summary>
        /// <returns>Tuple of pipId and wallTimeMs</returns>
        private Tuple<int, TimeSpan> ReadExecutionItem(JsonTextReader reader)
        {
            int pipId = -1;
            TimeSpan executionTime = TimeSpan.FromTicks(0);

            ExpectTokenType(reader, JsonToken.StartObject);
            reader.Read();

            while (reader.TokenType != JsonToken.EndObject)
            {
                if (CurrentValueMatches(reader, "processWallTime"))
                {
                    // wall time is in ticks which will overflow an int32 in about an hour of runtime
                    string wallTime = reader.ReadAsString();
                    long longWallTime;
                    if (!long.TryParse(wallTime, out longWallTime))
                    {
                        throw new MimicGeneratorException("Failed to parse processWallTime to numeric for PipId:{0} at line: {1}", pipId, reader.LineNumber);
                    }

                    executionTime = TimeSpan.FromTicks(longWallTime);
                    m_graph.PipDurationStats.Add(longWallTime);
                }
                else if (CurrentValueMatches(reader, "pipId"))
                {
                    pipId = reader.ReadAsInt32().Value;
                }
                else if (CurrentValueMatches(reader, "io"))
                {
                    while (reader.TokenType != JsonToken.EndObject)
                    {
                        reader.Read();
                    }
                }
                else if (CurrentValueMatches(reader, "startTime") || CurrentValueMatches(reader, "endTime"))
                {
                    long startOrEndTime = ReadAsLong(reader);
                    m_graph.BuildInterval.Add(startOrEndTime);
                }

                reader.Read();
            }

            return new Tuple<int, TimeSpan>(pipId, executionTime);
        }

        private static long ReadAsLong(JsonTextReader reader)
        {
            string value = reader.ReadAsString();
            long integralValue;
            if (!long.TryParse(value, out integralValue))
            {
                throw new MimicGeneratorException("Failed to parse Int64 for at line: {0}, column: {1}", reader.LineNumber, reader.LinePosition);
            }

            return integralValue;
        }

        /// <summary>
        /// Reads the graph array
        /// </summary>
        private void ReadGraphArray(JsonTextReader reader)
        {
            ExpectTokenType(reader, JsonToken.StartArray);
            reader.Read();

            while (reader.TokenType == JsonToken.StartObject)
            {
                ReadPip(reader);
            }

            ExpectTokenType(reader, JsonToken.EndArray);
            reader.Read();
        }

        /// <summary>
        /// Reads a pip from within the graph array
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA2204")]
        private void ReadPip(JsonTextReader reader)
        {
            ExpectTokenType(reader, JsonToken.StartObject);
            reader.Read();

            int pipId = -1;
            string stableId = null;
            string spec = null;
            List<int> produces = new List<int>();
            List<int> consumes = new List<int>();
            List<SemaphoreInfo> semaphores = new List<SemaphoreInfo>();
            string pipType = null;

            while (reader.TokenType != JsonToken.EndObject)
            {
                if (CurrentValueMatches(reader, "pipId"))
                {
                    pipId = reader.ReadAsInt32().Value;
                }

                if (CurrentValueMatches(reader, "stableId"))
                {
                    stableId = reader.ReadAsString();
                }
                else if (CurrentValueMatches(reader, "provenance"))
                {
                    reader.Read();
                    ExpectTokenType(reader, JsonToken.StartObject);
                    reader.Read();

                    while (reader.TokenType != JsonToken.EndObject)
                    {
                        if (CurrentValueMatches(reader, "spec"))
                        {
                            spec = reader.ReadAsString();
                        }

                        reader.Read();
                    }

                    ExpectTokenType(reader, JsonToken.EndObject);
                }
                else if (CurrentValueMatches(reader, "type"))
                {
                    pipType = reader.ReadAsString();
                }
                else if (CurrentValueMatches(reader, "consumes"))
                {
                    consumes = ReadIntArray(reader);
                }
                else if (CurrentValueMatches(reader, "produces"))
                {
                    produces = ReadIntArray(reader);
                }
                else if (CurrentValueMatches(reader, "semaphores"))
                {
                    semaphores = ReadSemaphoreArray(reader);
                }
                else
                {
                    // continue
                }

                reader.Read();
            }

            ExpectTokenType(reader, JsonToken.EndObject);
            reader.Read();

            if (pipType == "Process")
            {
                if (!m_graph.TryAddPip(new Process(pipId, stableId, spec, produces, consumes, semaphores)))
                {
                    throw new MimicGeneratorException("Encountered duplicate Process. PipId:{0}. Line:{1}", pipId, reader.LineNumber);
                }

                foreach (int produced in produces)
                {
                    m_graph.AddOutputArtifact(produced, pipId);
                }

                m_graph.ProcessPipStats.Add(1);
            }
            else if (pipType == "CopyFile")
            {
                if (consumes.Count != 1)
                {
                    throw new MimicGeneratorException("Encountered malformed CopyFile. PipId:{0}. Line:{1}", pipId, reader.LineNumber);
                }

                if (produces.Count != 1)
                {
                    Console.WriteLine("Warning. CopyFile pip with ID:{0} at line:{1} had no consumers of its destination. It will be skipped", pipId, reader.LineNumber);
                }
                else
                {
                    if (!m_graph.TryAddPip(new CopyFile(pipId, stableId, spec, consumes[0], produces[0])))
                    {
                        throw new MimicGeneratorException("Encountered duplicate CopyFile. PipId:{0}. Line{1}", pipId, reader.LineNumber);
                    }

                    m_graph.AddOutputArtifact(produces[0], pipId);
                }
            }
            else if (pipType == "WriteFile")
            {
                if (consumes.Count != 0)
                {
                    throw new MimicGeneratorException("Encountered malformed WriteFile. PipId:{0}. Line:{1}", pipId, reader.LineNumber);
                }

                if (produces.Count != 1)
                {
                    Console.WriteLine("Warning. WriteFile with ID:{0} at line:{1} had no consumers of its destination. It will be skipped", pipId, reader.LineNumber);
                }
                else
                {
                    if (!m_graph.TryAddPip(new WriteFile(pipId, stableId, spec, produces[0])))
                    {
                        throw new MimicGeneratorException("Encountered duplicate WriteFile. PipId:{0}. Line{1}", pipId, reader.LineNumber);
                    }

                    m_graph.AddOutputArtifact(produces[0], pipId);
                }
            }
            else if (pipType == "SealDirectory")
            {
                if (produces.Count != 1)
                {
                    Console.WriteLine("Warning. SealDirectory with ID:{0} at line:{1} had no consumers of its destination. It will be skipped", pipId, reader.LineNumber);
                }
                else
                {
                    if (!m_graph.TryAddPip(new SealDirectory(pipId, stableId, spec, produces[0])))
                    {
                        throw new MimicGeneratorException("Encountered duplicate SealDirectory. PipId:{0}. Line:{1}", pipId, reader.LineNumber);
                    }

                    m_graph.AddOutputArtifact(produces[0], pipId, isDirectory: true);
                }
            }
        }

        private void ReadArtifactArray(JsonTextReader reader)
        {
            ExpectTokenType(reader, JsonToken.StartArray);
            reader.Read();

            while (reader.TokenType == JsonToken.StartObject)
            {
                ReadArtifact(reader);
            }

            ExpectTokenType(reader, JsonToken.EndArray);
            reader.Read();
        }

        private void ReadArtifact(JsonTextReader reader)
        {
            ExpectTokenType(reader, JsonToken.StartObject);
            reader.Read();

            int id = -1;
            string file = null;
            string directory = null;
            List<int> contents = null;

            while (reader.TokenType != JsonToken.EndObject)
            {
                if (CurrentValueMatches(reader, "id"))
                {
                    id = reader.ReadAsInt32().Value;
                }
                else if (CurrentValueMatches(reader, "file"))
                {
                    file = reader.ReadAsString();
                }
                else if (CurrentValueMatches(reader, "directory"))
                {
                    directory = reader.ReadAsString();
                }
                else if (CurrentValueMatches(reader, "contents"))
                {
                    contents = ReadIntArray(reader);
                }
                else
                {
                    throw new MimicGeneratorException("Unrecognized property: {0}", reader.Value.ToString());
                }

                reader.Read();
            }

            if (file != null)
            {
                if (!m_graph.TryAddFile(id, new File(file)))
                {
                    throw new MimicGeneratorException("File artifact registered twice: " + file);
                }
            }
            else if (directory != null)
            {
                if (!m_graph.Directories.TryAdd(id, new Dir(directory, contents)))
                {
                    throw new MimicGeneratorException("Directory registered twice: " + directory);
                }
            }

            ExpectTokenType(reader, JsonToken.EndObject);
            reader.Read();
        }

        private static List<int> ReadIntArray(JsonTextReader reader)
        {
            List<int> list = new List<int>();

            reader.Read();
            ExpectTokenType(reader, JsonToken.StartArray);

            while (reader.TokenType != JsonToken.EndArray)
            {
                var value = reader.ReadAsInt32();
                if (value.HasValue)
                {
                    list.Add(value.Value);
                }
                else
                {
                    break;
                }
            }

            ExpectTokenType(reader, JsonToken.EndArray);

            return list;
        }

        private static List<SemaphoreInfo> ReadSemaphoreArray(JsonTextReader reader)
        {
            List<SemaphoreInfo> list = new List<SemaphoreInfo>();

            reader.Read();
            ExpectTokenType(reader, JsonToken.StartArray);
            reader.Read();

            while (reader.TokenType != JsonToken.EndArray)
            {
                list.Add(ReadSemaphore(reader));
                reader.Read();
            }

            ExpectTokenType(reader, JsonToken.EndArray);

            return list;
        }

        private static SemaphoreInfo ReadSemaphore(JsonTextReader reader)
        {
            ExpectTokenType(reader, JsonToken.StartObject);
            SemaphoreInfo semaphore = new SemaphoreInfo();
            reader.Read();

            while (reader.TokenType != JsonToken.EndObject)
            {
                if (CurrentValueMatches(reader, "name"))
                {
                    semaphore.Name = reader.ReadAsString();
                }
                else if (CurrentValueMatches(reader, "value"))
                {
                    semaphore.Value = reader.ReadAsInt32().Value;
                }
                else if (CurrentValueMatches(reader, "limit"))
                {
                    semaphore.Limit = reader.ReadAsInt32().Value;
                }
                else
                {
                    throw new MimicGeneratorException("Unrecognized property: {0}", reader.Value.ToString());
                }

                reader.Read();
            }

            ExpectTokenType(reader, JsonToken.EndObject);

            return semaphore;
        }

        private static void ExpectTokenType(JsonTextReader reader, JsonToken tokenType)
        {
            if (reader.TokenType != tokenType)
            {
                throw new MimicGeneratorException("Expected token type:{0}. Instead encountered:{1}. At line:{2}", tokenType.ToString(), reader.TokenType, reader.LineNumber);
            }
        }

        private static bool CurrentValueMatches(JsonTextReader reader, string value)
        {
            return reader.Value != null && reader.Value.ToString().Equals(value);
        }

        private void ReadObservedInputs()
        {
            Console.WriteLine("Parsing observed inputs");

            using (StreamReader sr = new StreamReader(m_observedInputsPath))
            {
                string semiStableId;
                string path;
                string type;
                string contentHash;
                while (!sr.EndOfStream)
                {
                    string pipLine = sr.ReadLine();
                    string[] split = pipLine.Split();
                    if (split.Length > 0)
                    {
                        semiStableId = split[0].TrimEnd(',').Replace(BuildXL.Pips.Operations.Pip.SemiStableHashPrefix, string.Empty);
                        if (string.IsNullOrWhiteSpace(semiStableId))
                        {
                            throw new MimicGeneratorException("Could not parse SemiStableId: {0}", pipLine);
                        }

                        int observedInputRecords;
                        string[] recordCountSplit = sr.ReadLine().Split(':');
                        if (recordCountSplit.Length != 2)
                        {
                            throw new MimicGeneratorException("Unexpected format for ObservedInputHashesByPath line: {0}", string.Join(":", recordCountSplit));
                        }

                        if (!int.TryParse(recordCountSplit[1], out observedInputRecords))
                        {
                            throw new MimicGeneratorException("Unexpected format for ObservedInputHashesByPath line: {0}", string.Join(":", recordCountSplit));
                        }

                        // Look up the pip by semiStableId
                        Pip p;
                        if (!m_graph.SemiStableHashes.TryGetValue(semiStableId, out p))
                        {
                            throw new MimicGeneratorException("Failed to find matching pip for semiStableId: {0}", semiStableId);
                        }

                        ObservedAccess[] accesses = new ObservedAccess[observedInputRecords];

                        for (int i = 0; i < observedInputRecords; i++)
                        {
                            path = ExtractKVP(sr.ReadLine(), "Path").Value;
                            contentHash = ExtractKVP(sr.ReadLine(), "ContentHash").Value;
                            type = ExtractKVP(sr.ReadLine(), "Type").Value;

                            ObservedAccess access = new ObservedAccess()
                            {
                                ObservedAccessType = ParseObservedAccess(type),
                                Path = path,
                                ContentHash = contentHash,
                            };

                            accesses[i] = access;
                        }

                        if (!m_graph.ObservedAccesses.TryAdd(p.PipId, accesses))
                        {
                            throw new MimicGeneratorException("Duplicate observed access for pip: {0}", semiStableId);
                        }

                        sr.ReadLine();
                    }
                    else
                    {
                        throw new MimicGeneratorException("Incorrect format for ObservedInputs line: ", pipLine);
                    }
                }
            }
        }

        private static KeyValuePair<string, string> ExtractKVP(string line, string expectedKeyName)
        {
            string[] split = line.Split('=');
            if (split.Length != 2)
            {
                throw new MimicGeneratorException("Line had unexpected format: {0}", line);
            }

            var result = new KeyValuePair<string, string>(split[0].Trim(' '), split[1].Trim(' '));

            if (result.Key != expectedKeyName)
            {
                throw new MimicGeneratorException("Line had unexpected format. Expected key '{0} in line: {1}", expectedKeyName, line);
            }

            return result;
        }

        private static ObservedAccessType ParseObservedAccess(string type)
        {
            switch (type)
            {
                case "DirectoryEnumeration":
                    return ObservedAccessType.DirectoryEnumeration;
                case "AbsentPathProbe":
                    return ObservedAccessType.AbsentPathProbe;
                case "FileContentRead":
                    return ObservedAccessType.FileContentRead;
                default:
                    throw new MimicGeneratorException("Unknown observed access type: {0}", type);
            }
        }
    }
}
