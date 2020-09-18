using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.Host.Service;
using Microsoft.AspNetCore.Mvc;

namespace BuildXL.Launcher.Server.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class DeploymentController : ControllerBase
    {
        private readonly ILogger _logger;
        private readonly DeploymentService _service;

        public DeploymentController(ILogger logger, DeploymentService service)
        {
            _logger = logger;
            _service = service;
        }

        [HttpPost]
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
    }
}
