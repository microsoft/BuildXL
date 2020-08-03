using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Host.Configuration;
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
        public Task<LauncherManifest> Get(DeploymentParameters parameters)
        {
            OperationContext context = new OperationContext(new Context(_logger));
            return _service.UploadFilesAndGetManifestAsync(context, parameters, waitForCompletion: false);
        }
    }
}
