// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Execution.Analyzer.Model
{
    public struct PipBasicInfo
    {
        public string pipId { get; set; }
        public string semiStableHash { get; set; }
        public string pipType { get; set; }
        public string shortDescription { get; set; }
    }
}
