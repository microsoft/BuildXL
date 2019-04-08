// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
