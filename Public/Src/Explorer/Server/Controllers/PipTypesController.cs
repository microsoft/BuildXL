// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using BuildXL.Pips.Operations;

namespace BuildXL.Explorer.Server.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class PipTypesController
    {
        [HttpGet]
        public ActionResult<IEnumerable<string>> Get()
        {
            return Enum.GetNames(typeof(PipType));
        }
    }
}
