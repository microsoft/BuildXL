// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace BuildXL.Launcher.Server
{
    /// <summary>
    /// Validates content cache download URLs against configured HTTPS origins.
    /// </summary>
    internal sealed class DownloadUrlPolicy
    {
        private readonly HashSet<string> m_allowedOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public DownloadUrlPolicy(IEnumerable<string> allowedOrigins)
        {
            foreach (string allowedOrigin in allowedOrigins ?? Array.Empty<string>())
            {
                if (!TryParseUri(allowedOrigin, out var uri)
                    || uri.AbsolutePath != "/"
                    || !string.IsNullOrEmpty(uri.Query)
                    || !string.IsNullOrEmpty(uri.Fragment))
                {
                    throw new ArgumentException(
                        $"Content cache download origin must be an absolute HTTPS origin without a path, query, fragment, or user information: '{allowedOrigin}'.",
                        nameof(allowedOrigins));
                }

                m_allowedOrigins.Add(GetOrigin(uri));
            }
        }

        public bool IsAllowed(string downloadUrl)
        {
            return TryParseUri(downloadUrl, out var uri)
                && string.IsNullOrEmpty(uri.Fragment)
                && m_allowedOrigins.Contains(GetOrigin(uri));
        }

        private static bool TryParseUri(string value, out Uri uri)
        {
            return Uri.TryCreate(value, UriKind.Absolute, out uri)
                && uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrEmpty(uri.UserInfo);
        }

        private static string GetOrigin(Uri uri)
        {
            return $"{uri.Scheme}://{uri.IdnHost}:{uri.Port}";
        }
    }
}
