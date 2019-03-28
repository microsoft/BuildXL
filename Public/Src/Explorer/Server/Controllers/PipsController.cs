// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using BuildXL.Explorer.Server.Analyzers;
using Microsoft.AspNetCore.Hosting;
using BuildXL.Execution.Analyzer.Model;

namespace BuildXL.Explorer.Server.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class PipsController : LoadController
    {
        public PipsController(IMemoryCache memoryCache, IHostingEnvironment env)
            : base(memoryCache, env)
        {
        }

        public PipsAnalyzer GetPipsAnalyzer(string xlgPath)
        {
            if (string.IsNullOrEmpty(xlgPath)) {
                throw new InvalidOperationException("xlg not valid");
            }

            var entry = Cache.GetOrCreate(
                     "PipsAnalyzer:" + xlgPath,
                (newEntry) =>
                {
                    var pipsAnalyzer = new PipsAnalyzer(GetAnalysisInput(xlgPath));
                    pipsAnalyzer.Prepare();
                    pipsAnalyzer.ReadExecutionLog(prepare: false);
                    pipsAnalyzer.Analyze();
                    return pipsAnalyzer;
                });

            return entry;
        }

        public string GetParameter(string key)
        {
            StringValues output;

            if (Request.Query.TryGetValue(key, out output) && output.Count == 1)
            {
                return output[0];
            }
            return null;           
        }

        [HttpGet]
        public ActionResult<IEnumerable<string>> Get(int draw, int start, int length, string xlg, string buildId)
        {
            var pipsAnalyzer = GetPipsAnalyzer(GetXlgPath(buildId, xlg));
            string sortDirection = "asc";
            string sortField = "";

            if (length < 0)
            {
                length = pipsAnalyzer.PipBasicInfoList.Count;
            }

            if (start < 0 || start > pipsAnalyzer.PipBasicInfoList.Count - 1)
            {
                start = 0;
            }

            // note: we only sort one column at a time
            var sortColumn = GetParameter("order[0][column]");
            if (sortColumn != null)
            {
                var sortColumnIndex = int.Parse(GetParameter("order[0][column]"));
                sortField = GetParameter($"columns[{sortColumnIndex}][data]");
                sortDirection = GetParameter("order[0][dir]");
            }

            var c = 0;
            string key = GetParameter($"columns[{c}][data]");
            string value = GetParameter($"columns[{c}][search][value]");
            Dictionary<string, string> coloumFilters = new Dictionary<string, string>();
            while (key != null)
            {
                if(value != null)
                {
                    coloumFilters.Add(key, value);
                }
                c++;
                key = GetParameter($"columns[{c}][data]");
                value = GetParameter($"columns[{c}][search][value]");
            }

            var processedPip = pipsAnalyzer.ProcessPips(sortField, sortDirection, coloumFilters);
            length = Math.Min(length, Math.Max(processedPip.Count - start, 0));
            var data = new PipData(processedPip.GetRange(start, length), pipsAnalyzer.PipBasicInfoList.Count, processedPip.Count, draw);

            return new JsonResult(data);
        }
    }


    internal class PipData
    {
        public List<PipBasicInfo> data;
        public int draw;
        public int recordsTotal;
        public int recordsFiltered;

        public PipData(List<PipBasicInfo> inputData, int totalBeforeFilter, int totalAfterFilter, int requestDraw)
        {
            data = new List<PipBasicInfo>(inputData);
            draw = requestDraw;
            recordsTotal = totalBeforeFilter;
            recordsFiltered = totalAfterFilter;
        }

    }
}
