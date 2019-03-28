// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using BuildXL.Execution.Analyzer;

namespace BuildXL.Explorer.Server.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class CriticalPathController : LoadController
    {
        public CriticalPathController(IMemoryCache memoryCache, IHostingEnvironment env)
            : base(memoryCache, env)
        {

        }

        /// GET criticalPath
        [HttpGet]
        public ActionResult<IEnumerable<string>> Get(string xlg, string buildId)
        {
            var criticalPathAnalyzer = new CriticalPathAnalyzer(GetAnalysisInput(GetXlgPath(buildId, xlg)), null);
            criticalPathAnalyzer.Prepare();
            criticalPathAnalyzer.ReadExecutionLog(prepare: false);
            criticalPathAnalyzer.Analyze();

            return new JsonResult(criticalPathAnalyzer.criticalPathData);
        }
    }
}
