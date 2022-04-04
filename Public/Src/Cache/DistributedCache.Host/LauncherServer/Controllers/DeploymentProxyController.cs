// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Mime;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.Host.Service;
using Microsoft.AspNetCore.Mvc;

namespace BuildXL.Launcher.Server.Controllers
{
    [ApiController]
    public class DeploymentProxyController : ControllerBase, IContentProxyController
    {
        private readonly ILogger _logger;
        private readonly DeploymentProxyService _service;

        public DeploymentProxyController(ILogger logger, DeploymentProxyService service)
        {
            _logger = logger;
            _service = service;
        }

        [HttpGet]
        [Route("content")]
        public async Task<ActionResult> GetContentAsync(string contextId, string hash, string accessToken)
        {
            OperationContext context = new OperationContext(new Context(contextId, _logger));

            var stream = await _service.GetContentAsync(context, hash, accessToken);
            return File(stream, MediaTypeNames.Application.Octet);
        }
    }
}
