// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

// We can't rename the Protobuff namespace so we'll have to keep these old global namespaces around.
namespace ContentStore.Grpc
{
    /// <nodoc />
    public partial class ResponseHeader
    {
        /// <nodoc />
        public ResponseHeader(DateTime serverReceiptTime, bool succeeded, int responseCode, string errorMessage, string diagnostics = null)
        {
            if (serverReceiptTime.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException(nameof(serverReceiptTime));
            }

            Succeeded = succeeded;
            Result = responseCode;
            ErrorMessage = errorMessage ?? string.Empty;
            Diagnostics = diagnostics ?? string.Empty;
            ServerReceiptTimeUtcTicks = serverReceiptTime.Ticks;
        }

        /// <nodoc />
        public static ResponseHeader Success(DateTime serverReceiptTime) => new ResponseHeader(serverReceiptTime, succeeded: true, responseCode: 0, errorMessage: null);

        /// <nodoc />
        public static ResponseHeader Failure(DateTime serverReceiptTime, string errorMessage, string diagnostics = null)
            => Failure(
                serverReceiptTime,
                responseCode: 1,
                errorMessage: errorMessage,
                diagnostics: diagnostics);

        /// <nodoc />
        public static ResponseHeader Failure(DateTime serverReceiptTime, int responseCode, string errorMessage, string diagnostics = null)
            => new ResponseHeader(
                serverReceiptTime,
                succeeded: false,
                responseCode: responseCode,
                errorMessage: errorMessage,
                diagnostics: diagnostics);

        /// <nodoc />
        public ResponseHeader(bool succeeded, int responseCode)
        {
            Succeeded = succeeded;
            Result = responseCode;
        }

        /// <nodoc />
        public ResponseHeader(bool succeeded)
        {
            Succeeded = succeeded;
        }
    }
}
