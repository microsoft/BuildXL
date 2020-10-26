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
    /// <summary>
    /// Interface defining method which must be exposed by services which recieve content proxy requests
    /// </summary>
    internal interface IContentProxyController
    {
        // NOTE: Attribute is purely to inform what route attribute should be on controller method
        [Route("content")]
        Task<ActionResult> GetContentAsync(Guid contextId, string hash, string accessToken);
    }
}
