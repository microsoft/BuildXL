// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Globalization;
using BuildXL.Pips;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using BuildXL.Execution.Analyzer;

namespace BuildXL.Explorer.Server.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class DumpPipController : LoadController
    {
        public DumpPipController(IMemoryCache memoryCache, IHostingEnvironment env)
            : base(memoryCache, env)
        {
        }

        /// GET dumpPip/pipHexId
        [HttpGet("{hexId}")]
        public ActionResult<string> Get(string hexId, string xlg, string buildId)
        {
            var id = uint.Parse(hexId, NumberStyles.HexNumber);
            var analysisInput = GetAnalysisInput(GetXlgPath(buildId, xlg));
            var pip = analysisInput.CachedGraph.PipTable.HydratePip(new PipId(id), PipQueryContext.ViewerAnalyzer);

            DumpPipAnalyzer dumpPipAnalyzer = new DumpPipAnalyzer(analysisInput, null, pip.SemiStableHash, false);
            dumpPipAnalyzer.Prepare();
            //var reader = Task.Run(() => dumpPipAnalyzer.ReadExecutionLog(prepare: false));
            //reader.Wait();

            return dumpPipAnalyzer.GetXDocument().ToString();
        }
    }
}
