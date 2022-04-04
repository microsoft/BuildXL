// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Mime;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.Host.Service;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BuildXL.Launcher.Server.Controllers
{
    [ApiController]
    public class ContentCacheController : ControllerBase
    {
        private readonly ILogger _logger;
        private readonly ContentCacheService _service;

        public ContentCacheController(ILogger logger, ContentCacheService service)
        {
            _logger = logger;
            _service = service;
        }

        [HttpGet]
        [Route("contentcache/getcontent")]
        public async Task<ActionResult> GetContentAsync(Guid contextId, string hash, string downloadUrl)
        {
            OperationContext context = new OperationContext(new Context(contextId, _logger));

            OpenStreamResult result = await _service.GetContentAsync(context, hash, downloadUrl);
            if (result.Succeeded)
            {
                return File(result.Stream, MediaTypeNames.Application.Octet);
            }
            else if (result.Code == OpenStreamResult.ResultCode.ContentNotFound)
            {
                return NotFound();
            }
            else
            {
                return StatusCode(StatusCodes.Status500InternalServerError, result.ToString());
            }

        }
    }
}
