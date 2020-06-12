// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration.Mutable;
using Newtonsoft.Json;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Defines a sorted data structure for the top N Process Pip's performance information needed for logging individual Pip's performance information into telemetry.
    /// Where N = <see cref="LoggingConfiguration.AriaIndividualMessageSizeLimitBytes"/> / <see cref="PerProcessPipMessageSizeBytes"/> * <see cref="LoggingConfiguration.MaxNumPipTelemetryBatches"/>
    /// </summary>
    public class PerProcessPipPerformanceInformationStore
    {
        /// <summary>
        /// Top N Pip performance info for telemetry logging
        /// </summary>
        private readonly ConcurrentBoundedSortedCollection<int, PerProcessPipPerformanceInformation> m_sortedTopPipPerformanceInfo;

        /// <summary>
        /// Specifies the internal max message size to be allowed for each individual message sent to Aria.
        /// </summary>
        private readonly int m_ariaCharLimit;

        /// <summary>
        /// Specifies the max number of batch message to be sent to Telemetry.
        /// </summary>
        private readonly int m_maxNumberOfBatches;

        /// <summary>
        /// Name set to the pip info array inside the final json string.
        /// </summary>
        private const string JsonPerfArrayPropertyName = "TopPipsPerformanceInfo";

        /// <summary>
        /// Approximate size of each process pip message inside the <see cref="JsonPerfArrayPropertyName"/> array
        /// </summary>
        private const int PerProcessPipMessageSizeBytes = 100;

        /// <summary>
        /// Constructs a sorted data structure of the given capacity to store top pip's performance information.
        /// </summary>
        public PerProcessPipPerformanceInformationStore(int maxNumberOfBatches, int ariaLimitBytes)
        {
            m_ariaCharLimit = ariaLimitBytes;
            m_maxNumberOfBatches = maxNumberOfBatches;
            int capacity = (m_ariaCharLimit / PerProcessPipMessageSizeBytes) * (maxNumberOfBatches + 1);  // maxNumberOfBatches + 1 avoids reporting fewer pips due to the approximation done by PerPipMessageSizeBytes
            m_sortedTopPipPerformanceInfo = new ConcurrentBoundedSortedCollection<int, PerProcessPipPerformanceInformation>(capacity,
                Comparer<int>.Create((a, b) => a.CompareTo(b)));  // To keep track of top N pips by execution times. ConcurrentBoundedSortedCollection only works with ascending order since it assumes the minimum is at index 0
        }

        /// <summary>
        /// Returns an array of json strings containing performance information about top N Pips
        /// </summary>
        public string[] GenerateTopPipPerformanceInfoJsonArray()
        {
            var sortedTopPipPerformanceInfo = m_sortedTopPipPerformanceInfo
                                                .Reverse()          // Log highest execution time pips first
                                                .Select(a => a.Value)
                                                .ToArray();

            if (sortedTopPipPerformanceInfo.Length == 0) { 
                return CollectionUtilities.EmptyArray<string>(); 
            }

            // final array of JSON strings to be returned at the end
            var jsonResp = new List<string>();

            var messageJsonPrefix = "{\"" + JsonPerfArrayPropertyName + "\":[";
            var messageJsonSuffix = "]}";

            int firstValidElementIndex = 0;
            string firstValidElement;
            do
            {
                firstValidElement = SerializePipPerfInfo(sortedTopPipPerformanceInfo[firstValidElementIndex]);
                firstValidElementIndex++;
            }
            while (firstValidElement.Length >= m_ariaCharLimit &&
                   firstValidElementIndex < sortedTopPipPerformanceInfo.Length);
            
            if (firstValidElementIndex == sortedTopPipPerformanceInfo.Length &&
                firstValidElement.Length >= m_ariaCharLimit)
            {
                // All values are larger than the telemetry message size limit
                return CollectionUtilities.EmptyArray<string>();
            }

            using var sbPool = Pools.GetStringBuilder();
            var currentMessage = sbPool.Instance;
            currentMessage.Append(messageJsonPrefix)
                .Append(firstValidElement);

            for (var i = firstValidElementIndex; i < sortedTopPipPerformanceInfo.Length && jsonResp.Count < m_maxNumberOfBatches; i++)
            {
                var pipPerfJson = SerializePipPerfInfo(sortedTopPipPerformanceInfo[i]);
                if(pipPerfJson.Length >= m_ariaCharLimit)
                {
                    // Skip pips with large description
                    continue;
                }
                var newMessageLength = currentMessage.Length + 1 + pipPerfJson.Length + messageJsonSuffix.Length;
                if (newMessageLength <= m_ariaCharLimit)
                {
                    // continue current batch
                    currentMessage.Append(",").Append(pipPerfJson);
                }
                else
                {
                    // finish the current batch and start a new one
                    currentMessage.Append(messageJsonSuffix);
                    jsonResp.Add(currentMessage.ToString());
                    currentMessage.Clear();
                    currentMessage.Append(messageJsonPrefix).Append(pipPerfJson);
                }
            }

            if (jsonResp.Count < m_maxNumberOfBatches)
            {
                currentMessage.Append(messageJsonSuffix);
                jsonResp.Add(currentMessage.ToString());
            }

            return jsonResp.ToArray();
        }


        /// <summary>
        /// Serialize Pip's Performance Information into Json Format
        /// </summary>
        public static string SerializePipPerfInfo(PerProcessPipPerformanceInformation perPipInfo)
        {
            return JsonConvert.SerializeObject(new Dictionary<string, object>
            {
                ["PipDescription"] = perPipInfo.PipDescription,
                ["TelemetryTags"] = perPipInfo.TelemetryTags,
                ["PipExecutionMs"] = perPipInfo.PipExecutionMs,
                ["PeakWorkingMemoryMb"] = perPipInfo.PeakWorkingMemoryMb,
                ["IOReadMb"] = perPipInfo.IOReadMb,
                ["IOWriteMb"] = perPipInfo.IOWriteMb
            });
        }

        /// <summary>
        /// Add a Pip into the builds top N pips. 
        /// Will be ignored if it's execution time was lower than the minumum execution time of existing N pips.
        /// </summary>
        public bool AddPip(PerProcessPipPerformanceInformation perPipInfo)
        {
            return m_sortedTopPipPerformanceInfo.TryAdd(perPipInfo.PipExecutionMs, perPipInfo);
        }
    }

    /// <summary>
    /// Defines the information needed for logging individual Process Pip's performance information into telemetry.
    /// </summary>
    public class PerProcessPipPerformanceInformation
    {
        /// <summary>
        /// Process Pip object
        /// </summary>
        public ProcessRunnablePip RunnablePip { get; }

        /// <summary>
        /// Process Pip's Description
        /// </summary>
        public string PipDescription => RunnablePip.Description;

        /// <summary>
        /// Pip level filtering tags
        /// </summary>
        public string[] TelemetryTags => RunnablePip.Pip.Tags
                                    .Select(a => a.ToString(RunnablePip.Environment.Context.StringTable))
                                    .Where(a => a.StartsWith(PipCountersByTelemetryTag.DefaultTelemetryTagPrefix))
                                    .ToArray() 
                                ?? CollectionUtilities.EmptyArray<string>();

        /// <summary>
        /// Difference between Pip's start time and end time in ms 
        /// </summary>
        public int PipExecutionMs { get; }

        /// <summary>
        /// Peak Working Set utilization by Pip in Mb
        /// </summary>
        public int PeakWorkingMemoryMb { get; }

        /// <summary>
        /// IO read operations in Mb
        /// </summary>
        public int IOReadMb { get; }

        /// <summary>
        /// IO write operations in Mb
        /// </summary>
        public int IOWriteMb { get; }

        /// <nodoc/>
        public PerProcessPipPerformanceInformation(
            ref ProcessRunnablePip runnablePip,
            int pipExecutionMs,
            int peakWorkingMemoryMb,
            int ioReadMb,
            int ioWriteMb)
        {
            RunnablePip = runnablePip;
            PipExecutionMs = pipExecutionMs;
            PeakWorkingMemoryMb = peakWorkingMemoryMb;
            IOReadMb = ioReadMb;
            IOWriteMb = ioWriteMb;
        }

        /// <nodoc/>
        public override bool Equals(object obj)
        {
            if (obj == null || !(obj is PerProcessPipPerformanceInformation))
            {
                return false;
            }

            var pipInfo = ((PerProcessPipPerformanceInformation)obj);
            return  (PipDescription == pipInfo.PipDescription) &&
                    TelemetryTags.SequenceEqual(pipInfo.TelemetryTags) &&
                    (PipExecutionMs == pipInfo.PipExecutionMs) &&
                    (PeakWorkingMemoryMb == pipInfo.PeakWorkingMemoryMb) &&
                    (IOReadMb == pipInfo.IOReadMb) &&
                    (IOWriteMb == pipInfo.IOWriteMb);
        }

        /// <nodoc/>
        public override int GetHashCode()
        {
            return  PipDescription.GetHashCode() ^
                    TelemetryTags.GetHashCode() ^
                    PipExecutionMs.GetHashCode() ^
                    PeakWorkingMemoryMb.GetHashCode() ^
                    IOReadMb.GetHashCode() ^
                    IOWriteMb.GetHashCode();
        }
    }
}