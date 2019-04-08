// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using Newtonsoft.Json;
using LanguageServer.Json;
using LanguageServer.Infrastructure.JsonDotNet;

#pragma warning disable SA1649 // File name must match first type name
#pragma warning disable SA1402 // File name must match first type name

namespace LanguageServer
{
    internal enum ResultTag
    {
        Success = 1,
        Error = 2,
    }

    /// <nodoc />
    public static class Result
    {
        /// <nodoc />
        public static Result<dynamic, ResponseError> Success { get; } = Result<dynamic, ResponseError>.Success(null);

        /// <nodoc />
        public static Result<TResult, ResponseError> InternalError<TResult>(string message)
        {
            Contract.Requires(!string.IsNullOrEmpty(message));

            return Result<TResult, ResponseError>.Error(
                new ResponseError()
                {
                    code = ErrorCodes.InternalError,
                    message = message,
                });
        }
    }

    /// <nodoc />
    [JsonConverter(typeof(ResultConverter))]
    public class Result<T, TError> : IResult
    {
        /// <nodoc />
        public static Result<T, TError> Success(T success)
        {
            return new Result<T, TError>(ResultTag.Success, success, default(TError));
        }

        /// <nodoc />
        public static Result<T, TError> Error(TError error)
        {
            return new Result<T, TError>(ResultTag.Error, default(T), error);
        }

        private readonly ResultTag _tag;
        private readonly T _success;
        private readonly TError _error;

        private Result(ResultTag tag, T success, TError error)
        {
            _tag = tag;
            _success = success;
            _error = error;
        }

        /// <nodoc />
        public object ErrorObject => _error;

        /// <nodoc />
        public object SuccessObject => _success;

        /// <nodoc />
        public T SuccessValue => _success;

        /// <nodoc />
        public TError ErrorValue => _error;

        /// <nodoc />
        public bool IsSuccess => _tag == ResultTag.Success;

        /// <nodoc />
        public bool IsError => _tag == ResultTag.Error;

        /// <nodoc />
        public Result<TResult, TError> Select<TResult>(Func<T, TResult> func) =>
            this.IsSuccess
                ? Result<TResult, TError>.Success(func(this.SuccessValue))
                : Result<TResult, TError>.Error(this.ErrorValue);

        /// <nodoc />
        public Result<TResult, TErrorResult> Select<TResult, TErrorResult>(Func<T, TResult> funcSuccess, Func<TError, TErrorResult> funcError) =>
            this.IsSuccess ? Result<TResult, TErrorResult>.Success(funcSuccess(this.SuccessValue)) :
            this.IsError ? Result<TResult, TErrorResult>.Error(funcError(this.ErrorValue)) :
            default(Result<TResult, TErrorResult>);

        /// <nodoc />
        public TResult Handle<TResult>(Func<T, TResult> funcSuccess, Func<TError, TResult> funcError) =>
            this.IsSuccess ? funcSuccess(this.SuccessValue) :
            this.IsError ? funcError(this.ErrorValue) :
            default(TResult);

        /// <nodoc />
        public void Handle(Action<T> actionSuccess, Action<TError> actionError)
        {
            if (this.IsSuccess)
            {
                actionSuccess(this.SuccessValue);
            }
            else if (this.IsError)
            {
                actionError(this.ErrorValue);
            }
        }

        /// <nodoc />
        public T HandleError(Func<TError, T> func) =>
            this.IsError
                ? func(this.ErrorValue)
                : this.SuccessValue;
    }

    /// <nodoc />
    public class VoidResult<TError> : IResult
    {
        /// <nodoc />
        public static VoidResult<TError> Success()
        {
            return new VoidResult<TError>(ResultTag.Success, default(TError));
        }

        /// <nodoc />
        public static VoidResult<TError> Error(TError error)
        {
            return new VoidResult<TError>(ResultTag.Error, error);
        }

        private readonly ResultTag _tag;
        private readonly TError _error;

        private VoidResult(ResultTag tag, TError error)
        {
            _tag = tag;
            _error = error;
        }

        /// <nodoc />
        public object ErrorObject => _error;

        /// <nodoc />
        public object SuccessObject => null;

        /// <nodoc />
        public TError ErrorValue => _error;

        /// <nodoc />
        public bool IsSuccess => _tag == ResultTag.Success;

        /// <nodoc />
        public bool IsError => _tag == ResultTag.Error;

        /// <nodoc />
        public Result<TResult, TError> Select<TResult>(Func<TResult> func) =>
            this.IsSuccess
                ? Result<TResult, TError>.Success(func())
                : Result<TResult, TError>.Error(this.ErrorValue);

        /// <nodoc />
        public Result<TResult, TErrorResult> Select<TResult, TErrorResult>(Func<TResult> funcSuccess, Func<TError, TErrorResult> funcError) =>
            this.IsSuccess ? Result<TResult, TErrorResult>.Success(funcSuccess()) :
            this.IsError ? Result<TResult, TErrorResult>.Error(funcError(this.ErrorValue)) :
            default(Result<TResult, TErrorResult>);

        /// <nodoc />
        public TResult Handle<TResult>(Func<TResult> funcSuccess, Func<TError, TResult> funcError) =>
            this.IsSuccess ? funcSuccess() :
            this.IsError ? funcError(this.ErrorValue) :
            default(TResult);

        /// <nodoc />
        public void Handle(Action actionSuccess, Action<TError> actionError)
        {
            if (this.IsSuccess)
            {
                actionSuccess();
            }
            else if (this.IsError)
            {
                actionError(this.ErrorValue);
            }
        }

        /// <nodoc />
        public void HandleError(Action<TError> actionError)
        {
            if (this.IsError) actionError(this.ErrorValue);
        }
    }
}
