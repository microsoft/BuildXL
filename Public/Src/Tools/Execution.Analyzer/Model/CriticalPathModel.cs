// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;

namespace BuildXL.Execution.Analyzer.Model
{
    public struct Node
    {
        public string pipId { get; set; }
        public string semiStableHash { get; set; }
        public string pipType { get; set; }
        public string startTime { get; set; }
        public string duration { get; set; }
        public string kernelTime { get; set; }
        public string userTime { get; set; }
        public string shortDescription { get; set; }
    }

    public struct ToolStat
    {
        public string name;
        public string time;
    }

    public class CriticalPath
    {
        public string time { get; set; }
        public List<ToolStat> toolStats { get; set; } = new List<ToolStat>();
        public List<Node> nodes { get; set; } = new List<Node>();
    }

    public class CriticalPathData
    {
        public List<CriticalPath> criticalPaths { get; set; } = new List<CriticalPath>();
        public List<Node> wallClockTopPips { get; set; } = new List<Node>();
        public List<Node> userTimeTopPips { get; set; } = new List<Node>();
        public List<Node> kernelTimeTopPips { get; set; } = new List<Node>();
    }
}
