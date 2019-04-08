// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Newtonsoft.Json;

namespace BuildXL.Explorer.Server
{
    /// <summary>
    /// Asp.Net filter for exceptions.
    /// </summary>
    /// <remarks>
    /// When we see an ExplorerException thrown we want to return a json structure with the error details so we
    /// can present it to the user. else aspnet returns an html page even though the request is an api request.
    /// </remarks>
    public class ExplorerExceptionFilter : IExceptionFilter
    {
        public void OnException(ExceptionContext context)
        {
            if (context.Exception is ExplorerException explorerException)
            {
                HttpResponse response = context.HttpContext.Response;
                response.StatusCode = 400;
                response.ContentType = "application/json";
                var err = JsonConvert.SerializeObject(new
                {
                    Message = explorerException.Message,
                    InnerException = explorerException.InnerException?.ToString(),
                });

                response.WriteAsync(err).GetAwaiter().GetResult();

                context.ExceptionHandled = true;
            }
        }
    }
}
