// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
