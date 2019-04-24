// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text.RegularExpressions;
using StackExchange.Redis;

namespace BuildXL.Cache.ContentStore.Distributed.Redis
{
    /// <summary>
    /// The result of a <see cref="IRedisBatch.GetCheckpointsInfoAsync"/> operation.
    /// </summary>
    internal class RedisCheckpointInfo
    {
        private const string ParseFieldNameRegexPattern = @"^Slot\#(?<slotNumber>\d+)\.(?<property>\w+)$";
        private static readonly Regex s_parseFieldNameRegex = new Regex(ParseFieldNameRegexPattern);

        private static readonly Dictionary<string, Action<RedisValue, RedisCheckpointInfo>> s_setValueMap =
            new Dictionary<string, Action<RedisValue, RedisCheckpointInfo>>()
            {
                [nameof(CheckpointId)] = (redisValue, result) => { result.CheckpointId = (string)redisValue; },
                [nameof(SequenceNumber)] = (redisValue, result) => { result.SequenceNumber = (long)redisValue; },
                [nameof(CheckpointCreationTime)] = (redisValue, result) => { result.CheckpointCreationTime = DateTime.FromFileTimeUtc((long)redisValue); },
                [nameof(MachineName)] = (redisValue, result) => { result.MachineName = (string)redisValue; },
            };

        /// <summary>
        /// The slot number of the checkpoint (for debugging purposes only).
        /// </summary>
        public int SlotNumber { get; }

        /// <summary>
        /// The checkpoint identifier for retrieving the checkpoint from storage.
        /// </summary>
        public string CheckpointId { get; private set; }

        /// <summary>
        /// The sequence number of the last event processed for the checkpoint.
        /// </summary>
        public long SequenceNumber { get; private set; }

        /// <summary>
        /// The date of creation for the checkpoint.
        /// </summary>
        public DateTime CheckpointCreationTime { get; private set; }

        /// <summary>
        /// The machine name.
        /// </summary>
        public string MachineName { get; private set; }

        private RedisCheckpointInfo(int slotNumber)
        {
            Contract.Requires(slotNumber >= 0);
            SlotNumber = slotNumber;
        }

        /// <nodoc />
        public RedisCheckpointInfo(string checkpointId, long sequenceNumber, DateTime checkpointCreationTime, string machineName, int slotNumber = -1)
        {
            Contract.Requires(!string.IsNullOrEmpty(checkpointId));
            Contract.Requires(!string.IsNullOrEmpty(machineName));

            SlotNumber = slotNumber;
            CheckpointId = checkpointId;
            SequenceNumber = sequenceNumber;
            CheckpointCreationTime = checkpointCreationTime;
            MachineName = machineName;
        }

        /// <summary>
        /// Parses the checkpoints given entries from the checkpoints hash map in Redis
        /// </summary>
        public static RedisCheckpointInfo[] ParseCheckpoints(HashEntry[] checkpointEntries)
        {
            List<RedisCheckpointInfo> checkpoints = new List<RedisCheckpointInfo>();
            Dictionary<string, RedisValue> entriesMap = checkpointEntries.ToDictionary(e => (string)e.Name, e => e.Value);
            HashSet<int> parsedSlots = new HashSet<int>();

            // The format of the name is 'Slot#{SlotNumber}.PropertyName' where PropertyName is
            // CheckpointId, SequenceNumber, CheckpointCreationTime or MachineName
            foreach (var entry in checkpointEntries)
            {
                string name = entry.Name;
                if (!name.StartsWith("Slot#"))
                {
                    continue;
                }

                var match = s_parseFieldNameRegex.Match(entry.Name);
                var property = match.Groups["property"].Value;
                int slotNumber = int.Parse(match.Groups["slotNumber"].Value);

                if (!match.Success)
                {
                    throw new FormatException($"Invalid field's name format. Field name '{name}' does not match regular expression '{ParseFieldNameRegexPattern}'.");
                }

                if (!s_setValueMap.ContainsKey(property))
                {
                    throw new FormatException($"Property '{property}' in field name '{name}' does not correspond to any of the expected property names: ['{string.Join("', '", s_setValueMap.Values)}']");
                }

                if (!parsedSlots.Add(slotNumber))
                {
                    // The slot is already processed.
                    continue;
                }

                var result = new RedisCheckpointInfo(slotNumber);

                // Parsing all the entries for a current slot.
                foreach (var setValueEntry in s_setValueMap)
                {
                    if (!entriesMap.TryGetValue($"Slot#{slotNumber}.{setValueEntry.Key}", out var propertyValue))
                    {
                        throw new FormatException($"Property '{setValueEntry.Key}' not found for slot '{slotNumber}'");
                    }

                    setValueEntry.Value(propertyValue, result);
                }

                checkpoints.Add(result);
            }

            return checkpoints.ToArray();
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            var slotPrefix = SlotNumber >= 0 ? $"SlotNumber: {SlotNumber}, " : "";
            return slotPrefix + $"CheckpointId: {CheckpointId}, SequenceNumber: {SequenceNumber}, CheckpointCreationTime: {CheckpointCreationTime.ToLocalTime()}, MachineName: {MachineName}";
        }
    }
}
