// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Tool.CloudTestClient
{
    /// <summary>
    /// Encapsulates the logic for retrieving historical job runtimes from Kusto and writing
    /// per-job runtime files. The <see cref="QueryRuntimesAsync"/> method can be overridden
    /// in tests to provide mock data without a live Kusto connection.
    /// </summary>
    internal abstract class HistoricRuntimeHelper
    {
        private static readonly char[] s_invalidFileNameChars = Path.GetInvalidFileNameChars();

        /// <summary>
        /// Number of days to look back for historical runtime data.
        /// </summary>
        public const int DefaultLookbackDays = 15;

        /// <summary>
        /// Maximum number of job IDs per Kusto query batch to stay within query size limits.
        /// </summary>
        public const int DefaultBatchSize = 1000;

        /// <summary>
        /// Queries Kusto (or a mock) for average runtimes of the given job IDs.
        /// Returns a dictionary mapping job ID → average duration in milliseconds.
        /// </summary>
        public abstract Task<Dictionary<string, long>> QueryRuntimesAsync(List<string> jobIds, CancellationToken cancellationToken);

        /// <summary>
        /// Extracts jobs from the session config, queries for runtimes, and writes per-job files.
        /// Returns true if files were written, false if there were no jobs to process.
        /// The <paramref name="cancellationToken"/> caps the (potentially slow) Kusto query; if it fires,
        /// an <see cref="OperationCanceledException"/> propagates to the caller.
        /// </summary>
        public async Task<bool> RetrieveAndWriteRuntimesAsync(string sessionConfigPath, string outputDir, bool debug, Action<string> log = null, CancellationToken cancellationToken = default)
        {
            var jobIdToName = ExtractJobsFromSessionConfig(sessionConfigPath);
            if (jobIdToName.Count == 0)
            {
                log?.Invoke("WARNING: No job IDs found in session config. No historic runtime files to write.");
                return false;
            }

            log?.Invoke($"Found {jobIdToName.Count} job(s) in session config. Querying for historical runtimes...");

            var jobIds = jobIdToName.Keys.ToList();
            var runtimes = await QueryRuntimesAsync(jobIds, cancellationToken);

            log?.Invoke($"Retrieved historical runtimes for {runtimes.Count} out of {jobIdToName.Count} job(s).");

            if (debug)
            {
                log?.Invoke("DEBUG: Historical runtimes retrieved:");
                foreach (var (jobId, avgDuration) in runtimes)
                {
                    var jobInfo = jobIdToName[jobId];
                    log?.Invoke($"  Job ID: {jobId}, Group: {jobInfo.GroupName}, Name: {jobInfo.JobName}, Avg Duration (ms): {avgDuration}");
                }
            }

            WriteHistoricRuntimeFiles(outputDir, runtimes, jobIdToName);
            log?.Invoke($"Historic runtimes written to {outputDir} ({jobIdToName.Count} file(s), {runtimes.Count} with data)");

            return true;
        }

        /// <summary>
        /// Extracts a mapping of job ID to (group name, job name) from a session config JSON file.
        /// The group name is the resolved name written by the session config generator (the explicit group name,
        /// or "&lt;image&gt; &lt;sku&gt;" when none was provided), and is captured because the same job name can appear in
        /// multiple groups within a session.
        /// </summary>
        public static Dictionary<string, (string GroupName, string JobName)> ExtractJobsFromSessionConfig(string sessionConfigPath)
        {
            var jobs = new Dictionary<string, (string GroupName, string JobName)>();
            string json = File.ReadAllText(sessionConfigPath);

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("dynamicGroupRequests", out var groups))
            {
                foreach (var group in groups.EnumerateArray())
                {
                    string groupName = group.TryGetProperty("groupName", out var groupNameElement)
                        ? groupNameElement.GetString()
                        : null;

                    if (group.TryGetProperty("dynamicJobRequests", out var jobRequests))
                    {
                        foreach (var job in jobRequests.EnumerateArray())
                        {
                            string id = job.TryGetProperty("jobId", out var idElement) ? idElement.GetString() : null;
                            string name = job.TryGetProperty("jobName", out var nameElement) ? nameElement.GetString() : null;

                            // Every job carries a jobId, jobName, and a resolved groupName.
                            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name) || string.IsNullOrEmpty(groupName))
                            {
                                throw new InvalidOperationException(
                                    $"Malformed job entry in session config file '{sessionConfigPath}': expected non-empty jobId, jobName, and groupName, but got jobId='{id}', jobName='{name}', groupName='{groupName}'.");
                            }

                            jobs[id] = (groupName, name);
                        }
                    }
                }
            }

            return jobs;
        }

        /// <summary>
        /// Writes one JSON file per job into the specified directory.
        /// Each file is named after the sanitized "&lt;group&gt;_&lt;job&gt;" and contains { "avgDurationMs": N }.
        /// Jobs without runtime data get a file with avgDurationMs of -1.
        /// </summary>
        public static void WriteHistoricRuntimeFiles(string outputDir, IReadOnlyDictionary<string, long> runtimes, IReadOnlyDictionary<string, (string GroupName, string JobName)> jobIdToInfo)
        {
            Directory.CreateDirectory(outputDir);

            foreach (var (jobId, jobInfo) in jobIdToInfo)
            {
                long avgDuration = runtimes.TryGetValue(jobId, out long value) ? value : -1;
                string fileName = GetHistoricRuntimeFileName(jobInfo.GroupName, jobInfo.JobName);
                string filePath = Path.Combine(outputDir, fileName);
                string json = $"{{\"avgDurationMs\":{avgDuration}}}";
                File.WriteAllText(filePath, json);
            }
        }

        /// <summary>
        /// Ensures an (empty) runtime file exists for every job, creating a zero-length file for any job that
        /// does not already have one. Used as a fallback when runtime retrieval fails. The reading side treats an empty file as "no historic data".
        /// </summary>
        public static void WriteEmptyHistoricRuntimeFiles(string outputDir, IReadOnlyDictionary<string, (string GroupName, string JobName)> jobIdToInfo, Action<string> log = null)
        {
            Directory.CreateDirectory(outputDir);

            int created = 0;
            foreach (var (_, jobInfo) in jobIdToInfo)
            {
                string fileName = GetHistoricRuntimeFileName(jobInfo.GroupName, jobInfo.JobName);
                string filePath = Path.Combine(outputDir, fileName);
                if (!File.Exists(filePath))
                {
                    File.WriteAllText(filePath, string.Empty);
                    created++;
                }
            }

            log?.Invoke($"Wrote {created} empty historic runtime file(s) to '{outputDir}'.");
        }

        /// <summary>
        /// Reads a per-job historic runtime JSON file and returns the avgDurationMs value,
        /// or null if the file is empty or doesn't contain the expected property.
        /// </summary>
        public static long? ReadRuntimeFromFile(string filePath)
        {
            string json = File.ReadAllText(filePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                // An empty file signals that historic runtime retrieval failed for this job; treat as no data.
                return null;
            }

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("avgDurationMs", out var durationElement))
            {
                return durationElement.GetInt64();
            }

            return null;
        }

        /// <summary>
        /// Builds the per-job historic runtime file name from the (resolved) group name and job name, as
        /// "&lt;sanitizedGroup&gt;_&lt;sanitizedJob&gt;.json".
        /// </summary>
        public static string GetHistoricRuntimeFileName(string groupName, string jobName)
            => SanitizeFileName(groupName) + "_" + SanitizeFileName(jobName) + ".json";

        /// <summary>
        /// Sanitizes a job name into a filename safe for both Windows and Linux.
        /// Replaces characters that are invalid in file paths with underscores.
        /// </summary>
        public static string SanitizeFileName(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                sb.Append(Array.IndexOf(s_invalidFileNameChars, c) >= 0 ? '_' : c);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Splits a list into batches of the specified size.
        /// </summary>
        public static List<List<T>> Batch<T>(List<T> source, int batchSize)
        {
            var batches = new List<List<T>>();
            for (int i = 0; i < source.Count; i += batchSize)
            {
                batches.Add(source.GetRange(i, Math.Min(batchSize, source.Count - i)));
            }

            return batches;
        }
    }
}
