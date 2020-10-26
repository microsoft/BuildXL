using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.Host.Service;
using Microsoft.AspNetCore.Mvc;

namespace BuildXL.Launcher.Server.Controllers
{
    [ApiController]
    public class DeploymentController : ControllerBase, IContentProxyController
    {
        private readonly ILogger _logger;
        private readonly DeploymentService _service;

        public DeploymentController(ILogger logger, DeploymentService service)
        {
            _logger = logger;
            _service = service;
        }

        [HttpGet]
        [Route("deploymentChangeId")]
        public async Task<ActionResult> GetDeploymentChangeIdAsync()
        {
            await Task.Yield();

            var manifestId = _service.ReadManifestChangeId();
            return Ok(manifestId);
        }

        [HttpPost]
        [Route("deployment")]
        public async Task<ActionResult> GetAsync(DeploymentParameters parameters)
        {
            OperationContext context = new OperationContext(new Context(parameters.ContextId, _logger));

            if (!await _service.IsAuthorizedAsync(context, parameters))
            {
                return Unauthorized();
            }

            var result = await _service.UploadFilesAndGetManifestAsync(context, parameters, waitForCompletion: false);
            return new JsonResult(result, DeploymentUtilities.ConfigurationSerializationOptions);
        }

        [HttpPost]
        [Route("getproxyaddress")]
         public ActionResult GetProxyAddress(Guid contextId, string accessToken, [FromBody] HostParameters parameters)
        {
            OperationContext context = new OperationContext(new Context(contextId, _logger));

            //  Use download token to authenticate.
            if (!_service.TryGetDownloadUrl(context, accessToken: accessToken, traceInfo: parameters.ToString()).Succeeded)
            {
                return Unauthorized();
            }

            return Ok(_service.GetProxyBaseAddress(context, parameters));
        }

        [HttpGet]
        [Route("content")]
        public async Task<ActionResult> GetContentAsync(Guid contextId, string hash, string accessToken)
        {
            await Task.Yield();

            OperationContext context = new OperationContext(new Context(contextId, _logger));
            if (!_service.TryGetDownloadUrl(context, accessToken: accessToken, traceInfo: $"Hash={hash}").TryGetValue(out var downloadUrl))
            {
                return Unauthorized();
            }

            return Redirect(downloadUrl);
        }
    }
}
