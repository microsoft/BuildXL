// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.AspNetCore.Mvc;

namespace BuildXL.Explorer.Server.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class TestController : ControllerBase
    {
        [HttpGet]
        public ActionResult<TestResult> Test()
        {
            return new TestResult
            {
                Status = "Online",
                Version = BuildXL.Utilities.Branding.Version,
            };
        }
    }

    public class TestResult
    {
        public string Status { get; set; }

        public string Version { get; set; }
    }
}
