// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Execution.Analyzer;
using BuildXL.Execution.Analyzer.Model;

namespace BuildXL.Explorer.Server.Analyzers
{

    /// <summary>
    /// Class for extracting pips data.
    /// </summary>
    public class PipsAnalyzer : Analyzer
    {
        public List<PipBasicInfo> PipBasicInfoList = new List<PipBasicInfo>();
        private PipTable m_pipTable;
        private Dictionary<ModuleId, string> m_moduleIdToFriendlyName = new Dictionary<ModuleId, string>();

        /// <nodoc />
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        public PipsAnalyzer(AnalysisInput input)
            : base(input)
        {
            m_pipTable = input.CachedGraph.PipTable;
        }

        /// <inheritdoc />
        public override int Analyze()
        {
            PipBasicInfoList.Clear();
            m_moduleIdToFriendlyName.Clear();
            foreach (var pipId in m_pipTable.Keys)
            {
                var pipType = m_pipTable.GetPipType(pipId);
                if (pipType == PipType.Process || pipType == PipType.CopyFile)
                {
                    var pipBasicInfo = new PipBasicInfo();
                    pipBasicInfo.pipId = pipId.Value.ToString("X16", CultureInfo.InvariantCulture).TrimStart(new[] { '0' });
                    pipBasicInfo.semiStableHash = m_pipTable.GetPipSemiStableHash(pipId).ToString("X");
                    pipBasicInfo.pipType = pipType.ToString();

                    var pip = m_pipTable.HydratePip(pipId, PipQueryContext.ViewerAnalyzer);
                    pipBasicInfo.shortDescription = pip.GetShortDescription(Input.CachedGraph.PipGraph.Context);

                    PipBasicInfoList.Add(pipBasicInfo);
                }
            }
            return 0;
        }

        public List<PipBasicInfo> ProcessPips(string orderingField, string orderingDir, Dictionary<string, string> coloumFilters)
        {

            List<PipBasicInfo> pipsBasicInfoListProcessed = new List<PipBasicInfo>(PipBasicInfoList);
            foreach (var coloumFilter in coloumFilters)
            {
                string columnKey = coloumFilter.Key;
                string filter = coloumFilter.Value.ToLower();
                var columnProperty = typeof(PipBasicInfo).GetProperty(columnKey);

                if (columnProperty != null)
                {
                    pipsBasicInfoListProcessed = pipsBasicInfoListProcessed.Where(o => ((string)columnProperty.GetValue(o)).ToLower().Contains(filter)).ToList<PipBasicInfo>();
                }
            }

            var orderingProperty = typeof(PipBasicInfo).GetProperty(orderingField);
            if (orderingProperty != null)
            {
                if (orderingDir == "desc")
                {
                    pipsBasicInfoListProcessed = pipsBasicInfoListProcessed.OrderByDescending(o => orderingProperty.GetValue(o)).ToList<PipBasicInfo>();
                }
                else if (orderingDir == "asc")
                {
                    pipsBasicInfoListProcessed = pipsBasicInfoListProcessed.OrderBy(o => orderingProperty.GetValue(o)).ToList<PipBasicInfo>();
                }
            }

            return pipsBasicInfoListProcessed;
        }
    }
}
