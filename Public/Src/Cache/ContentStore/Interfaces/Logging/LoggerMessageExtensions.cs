// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.CompilerServices;

namespace BuildXL.Cache.ContentStore.Interfaces.Logging
{
    /// <nodoc/>
    public static class LoggerMessageExtensions
    {
        /// <summary>
        /// Standardizes the format that goes to a regular <see cref="ILogger"/> in terms of message provenance
        /// </summary>
        public static void TraceMessage(this ILogger logger, string id, Severity severity, string message, Exception? exception, string component, [CallerMemberName] string? operation = null)
        {
            string? provenance;
            if ((!string.IsNullOrEmpty(component) && message.StartsWith(component)) || (!string.IsNullOrEmpty(operation) && message.Contains(operation)))
            {
                provenance = string.Empty;
            }
            else
            {
                if (string.IsNullOrEmpty(component) || string.IsNullOrEmpty(operation))
                {
                    provenance = $"{component}{operation}: ";

                    if (provenance.Equals(": "))
                    {
                        provenance = string.Empty;
                    }
                }
                else
                {
                    provenance = $"{component}.{operation}: ";
                }
            }

            if (exception == null)
            {
                logger.Log(severity, $"{id} {provenance}{message}");
            }
            else
            {
                if (severity == Severity.Error)
                {
                    logger.Error(exception, $"{id} {provenance}{message}");
                }
                else
                {
                    logger.Log(severity, $"{id} {provenance}{message} {exception}");
                }
            }
        }
    }
}
