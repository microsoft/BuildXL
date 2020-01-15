// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BuildXL.Explorer.Server.Models
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum BuildState
    {
        ConstructingGraph,
        Runningpips,
        Passed,
        PassedWithWarnings,
        Failed,
    }
}
