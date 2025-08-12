// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities.Core;

namespace Tool.BlobDaemon
{
    internal struct UploadLocation
    {
        public UploadLocationKind LocationKind { get; }
        public string Uri { get; }
        public string Account { get; }
        public string Container { get; }
        public string RelativePath { get; }

        private UploadLocation(UploadLocationKind locationKind, string uri, string account, string container, string relativePath)
        {
            LocationKind = locationKind;
            Uri = uri;
            Account = account;
            Container = container;
            RelativePath = relativePath;
        }

        public static UploadLocation CreateUriBased(string uri)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(uri));
            return new UploadLocation(UploadLocationKind.UriBased, uri, null, null, null);
        }

        public static UploadLocation CreateContainerBased(string account, string container, string relativePath)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(account));
            Contract.Requires(!string.IsNullOrWhiteSpace(container));
            Contract.Requires(!string.IsNullOrWhiteSpace(relativePath));
            return new UploadLocation(UploadLocationKind.ContainerBased, null, account, container, relativePath);
        }

        public static Possible<UploadLocation> TryParse(string uploadLocation)
        {
            /*
             * Format (codesync: Public\Src\Tools\BlobDaemon\Tool.BlobDaemonRunner.dsc)
             * #uri#${uri.uri}#
             * or
             * #container#accountName#containerName#uploadPath#
             */

            if (string.IsNullOrWhiteSpace(uploadLocation))
            {
                return new Failure<string>("Upload location cannot be null or empty");
            }

            var parts = uploadLocation.Split('#', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[0] == "uri" && !string.IsNullOrWhiteSpace(parts[1])) // uri target
            {
                // Check that the string is a valid URI.
                if (!System.Uri.TryCreate(parts[1], UriKind.Absolute, out var _))
                {
                    return new Failure<string>($"Invalid URI '{parts[1]}' in upload location '{uploadLocation}'");
                }

                return CreateUriBased(parts[1]);
            }
            else if (parts.Length == 4 && parts[0] == "container"
                && !string.IsNullOrWhiteSpace(parts[1]) && !string.IsNullOrWhiteSpace(parts[2]) && !string.IsNullOrWhiteSpace(parts[3])) // container target
            {
                return CreateContainerBased(parts[1], parts[2], parts[3]);
            }
            else
            {
                return new Failure<string>($"Invalid upload location '{uploadLocation}'");
            }
        }
    }

    internal enum UploadLocationKind
    {
        UriBased,
        ContainerBased
    }
}
