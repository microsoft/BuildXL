// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace BuildXL.LogGenerator
{
    /// <summary>
    /// A configuration used by <see cref="LogGenerator"/> during log generation.
    /// </summary>
    internal record LogGenConfiguration
    {
        [JsonProperty("aliases")]
        public KeyValuePair[] Aliases { get; init; } = Array.Empty<KeyValuePair>();

        [JsonProperty("generationNamespace")]
        public string GenerationNamespace { get; init; } = string.Empty;

        [JsonProperty("targetFramework")]
        public string TargetFramework { get; init; } = string.Empty;

        [JsonProperty("targetRuntime")]
        public string TargetRuntime { get; init; } = string.Empty;

        /// <inheritdoc />
        public override string ToString()
        {
            var sb = new System.Text.StringBuilder();

            sb.AppendLine($"{nameof(Aliases)}: [{string.Join((string)", ", (IEnumerable<string>)Aliases.Select(a => a.ToString()))}]")
                .AppendLine($"{nameof(GenerationNamespace)}: {GenerationNamespace}")
                .AppendLine($"{nameof(TargetFramework)}: {TargetFramework}")
                .AppendLine($"{nameof(TargetRuntime)}: {TargetRuntime}");


            return sb.ToString();
        }
    }
    internal record KeyValuePair
    {
        [JsonProperty("key")]
        public string Key { get; init; } = string.Empty;

        [JsonProperty("value")]
        public string Value { get; init; } = string.Empty;

        /// <inheritdoc />
        public override string ToString()
        {
            return $"{nameof(Key)}: {Key}, {nameof(Value)}: {Value}";
        }
    }
}