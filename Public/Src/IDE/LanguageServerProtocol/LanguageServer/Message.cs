// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using LanguageServer.Json;

#pragma warning disable SA1402 // File may only contain a single type

namespace LanguageServer
{
    /// <nodoc />
    public static class Message
    {
        /// <nodoc />
        public static Result<T, TError> ToResult<T, TError>(ResponseMessage<T, TError> response)
            where TError : ResponseError
        {
            return (response.error == null)
                ? Result<T, TError>.Success(response.result)
                : Result<T, TError>.Error(response.error);
        }

        /// <nodoc />
        public static VoidResult<TError> ToResult<TError>(VoidResponseMessage<TError> response)
            where TError : ResponseError
        {
            return (response.error == null)
                ? VoidResult<TError>.Success()
                : VoidResult<TError>.Error(response.error);
        }

        /// <nodoc />
        public static ResponseError ParseError(string message = "Parse error") => new ResponseError { code = ErrorCodes.ParseError, message = message };

        /// <nodoc />
        public static ResponseError<T> ParseError<T>(T data, string message = "Parse error") => new ResponseError<T> { code = ErrorCodes.ParseError, message = message, data = data };

        /// <nodoc />
        public static ResponseError InvalidRequest(string message = "Invalid Request") => new ResponseError { code = ErrorCodes.InvalidRequest, message = message };

        /// <nodoc />
        public static ResponseError<T> InvalidRequest<T>(T data, string message = "Invalid Request") => new ResponseError<T> { code = ErrorCodes.InvalidRequest, message = message, data = data };

        /// <nodoc />
        public static ResponseError MethodNotFound(string message = "Method not found") => new ResponseError { code = ErrorCodes.MethodNotFound, message = message };

        /// <nodoc />
        public static ResponseError<T> MethodNotFound<T>(T data, string message = "Method not found") => new ResponseError<T> { code = ErrorCodes.MethodNotFound, message = message, data = data };

        /// <nodoc />
        public static ResponseError InvalidParams(string message = "Invalid params") => new ResponseError { code = ErrorCodes.InvalidParams, message = message };

        /// <nodoc />
        public static ResponseError<T> InvalidParams<T>(T data, string message = "Invalid params") => new ResponseError<T> { code = ErrorCodes.InvalidParams, message = message, data = data };

        /// <nodoc />
        public static ResponseError ServerError(ErrorCodes code) => new ResponseError { code = code, message = "Server error" };

        /// <nodoc />
        public static ResponseError<T> ServerError<T>(ErrorCodes code, T data) => new ResponseError<T> { code = code, message = "Server error", data = data };

        /// <nodoc />
        public static ResponseError<T> ServerError<T>(string message = "Server error") => new ResponseError<T> { code = ErrorCodes.ServerErrorStart, message = message };
    }

    internal class MessageTest
    {
        public string jsonrpc { get; set; }

        public NumberOrString id { get; set; }

        public string method { get; set; }

        public bool IsMessage => jsonrpc == "2.0";

        public bool IsRequest => (IsMessage && id != null && method != null);

        public bool IsResponse => (IsMessage && id != null && method == null);

        public bool IsNotification => (IsMessage && id == null && method != null);

        public bool IsCancellation => (IsNotification && method == "$/cancelRequest");
    }

    /// <nodoc />
    public abstract class MessageBase
    {
        /// <nodoc />
        public string jsonrpc { get; set; } = "2.0";
    }

    /// <nodoc />
    public abstract class MethodCall : MessageBase
    {
        /// <nodoc />
        public string method { get; set; }
    }

    /// <nodoc />
    public abstract class RequestMessageBase : MethodCall
    {
        /// <nodoc />
        public NumberOrString id { get; set; }
    }

    /// <nodoc />
    public class VoidRequestMessage : RequestMessageBase
    {
    }

    /// <nodoc />
    public class RequestMessage<T> : RequestMessageBase
    {
        /// <nodoc />
        public T @params { get; set; }
    }

    /// <nodoc />
    public abstract class ResponseMessageBase : MessageBase
    {
        /// <nodoc />
        public NumberOrString id { get; set; }
    }

    /// <nodoc />
    public class VoidResponseMessage<TError> : ResponseMessageBase
        where TError : ResponseError
    {
        /// <nodoc />
        public TError error { get; set; }
    }

    /// <nodoc />
    public class ResponseMessage<T, TError> : ResponseMessageBase
        where TError : ResponseError
    {
        /// <nodoc />
        public T result { get; set; }

        /// <nodoc />
        public TError error { get; set; }
    }

    /// <nodoc />
    public abstract class NotificationMessageBase : MethodCall
    {
    }

    /// <nodoc />
    public class VoidNotificationMessage : NotificationMessageBase
    {
    }

    /// <nodoc />
    public class NotificationMessage<T> : NotificationMessageBase
    {
        /// <nodoc />
        public T @params { get; set; }
    }

    /// <nodoc />
    public class ResponseError
    {
        // TODO: Note, the ErrorCodes enum are the defined set that is reserved
        // TODO: By JSON-RPC, the error code in reality should be a int
        // TODO: As anything not in the reserved list can be used for application defined errors
        /// <nodoc />
        public ErrorCodes code { get; set; }

        /// <nodoc />
        public string message { get; set; }
    }

    /// <nodoc />
    public class ResponseError<T> : ResponseError
    {
        /// <nodoc />
        public T data { get; set; }
    }

    /// <nodoc />
    public enum ErrorCodes
    {
        /// <nodoc />
        ParseError = -32700,
        /// <nodoc />
        InvalidRequest = -32600,
        /// <nodoc />
        MethodNotFound = -32601,
        /// <nodoc />
        InvalidParams = -32602,
        /// <nodoc />
        InternalError = -32603,
        /// <nodoc />
        ServerErrorStart = -32099,
        /// <nodoc />
        ServerErrorEnd = -32000,
        /// <nodoc />
        ServerNotInitialized = -32002,
        /// <nodoc />
        UnknownErrorCode = -32001,
        /// <nodoc />
        RequestCancelled = -32800,
    }
}
