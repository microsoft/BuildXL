// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace BuildXL.Cache.Logging.External
{
    /// <summary>
    /// Keeps a mapping between well known exception types and messages from corresponding exception types
    /// </summary>
    internal class TaskExceptionObserver
    {
        private readonly Dictionary<string, List<string>> _wellKnownExceptions = new Dictionary<string, List<string>>();

        public TaskExceptionObserver()
        {
            AddExceptionHelper("Grpc.Core.RpcException", "StatusCode=Unavailable");
            AddExceptionHelper("Grpc.Core.RpcException", "StatusCode=Unknown");
            AddExceptionHelper("Grpc.Core.RpcException", "StatusCode=Cancelled");
            IgnoreWindowsStorageByteCountingStreamError();
        }

        /// <summary>
        /// Determines whether the exception is well known, if the exception's message contains a mapped error message.
        /// In the case of AggregateExceptions, all inner exceptions must be well known to return true.
        /// </summary>
        public bool IsWellKnownException(Exception e)
        {
            if (e is AggregateException aggException)
            {
                foreach (var innerException in aggException.InnerExceptions)
                {
                    if (!IsWellKnownExceptionHelper(innerException.GetType().ToString(), innerException.Message))
                    {
                        return false;
                    }
                }

                return true;
            }

            return IsWellKnownExceptionHelper(e.GetType().ToString(), e.Message);
        }
        
        private bool IsWellKnownExceptionHelper(string exceptionType, string exceptionMsg)
        {
            if (_wellKnownExceptions.ContainsKey(exceptionType))
            {
                var exceptionMsgList = _wellKnownExceptions[exceptionType];
                foreach (var msg in exceptionMsgList)
                {
                    if (exceptionMsg.Contains(msg))
                    {
                        return true;
                    }
                }

                return false;
            }

            return false;
        }

        /// <summary>
        /// Adding a new key value pair between exception type and error message
        /// In the case of AggregateExceptions, we add all inner exceptions to the mapping.
        /// </summary>
        public void AddException(Exception e)
        {
            if (e is AggregateException aggException)
            {
                foreach (var innerException in aggException.InnerExceptions)
                {
                    AddExceptionHelper(innerException.GetType().ToString(), innerException.Message);
                }
            }

            AddExceptionHelper(e.GetType().ToString(), e.Message);
        }

        private void AddExceptionHelper(string exceptionType, string msg)
        {
            if (_wellKnownExceptions.ContainsKey(exceptionType))
            {
                var exceptionMsgList = _wellKnownExceptions[exceptionType];
                if (!exceptionMsgList.Contains(msg))
                {
                    exceptionMsgList.Add(msg);
                }
            }
            else
            {
                _wellKnownExceptions.Add(exceptionType, new List<string>() { msg });
            }
        }

        private void IgnoreWindowsStorageByteCountingStreamError()
        {
            // Due to some bugs in the current version of Azure Storage
            // ByteCountingStream.EndRead may fail.
            // This was fixed in AzureStorage version 11.1.4.
            // Remove this once the codebase is migrated to that version.
            // Work Item: 1739732
            AddExceptionHelper("System.Net.WebException", "at Microsoft.WindowsAzure.Storage.Core.ByteCountingStream.EndRead(IAsyncResult asyncResult)");
            // Here is an example of a full stack trace:
            //            Exception has occurred in an unobserved task. Process may exit. Exception=[System.AggregateException: A Task's exception(s) were not observed either by Waiting on the Task or accessing its Exception property. As a result, the unobserved exception was rethrown by the finalizer thread. ---> System.Net.WebException: The request was aborted: The request was canceled.
            //   at System.Net.ConnectStream.EndRead(IAsyncResult asyncResult)
            //   at Microsoft.WindowsAzure.Storage.Core.ByteCountingStream.EndRead(IAsyncResult asyncResult)
            //   at System.Threading.Tasks.TaskFactory`1.FromAsyncTrimPromise`1.Complete(TInstance thisRef, Func`3 endMethod, IAsyncResult asyncResult, Boolean requiresSynchronization)
            //   --- End of inner exception stack trace ---
            //---> (Inner Exception #0) System.Net.WebException: The request was aborted: The request was canceled.
            //   at System.Net.ConnectStream.EndRead(IAsyncResult asyncResult)
            //   at Microsoft.WindowsAzure.Storage.Core.ByteCountingStream.EndRead(IAsyncResult asyncResult)
            //   at System.Threading.Tasks.TaskFactory`1.FromAsyncTrimPromise`1.Complete(TInstance thisRef, Func`3 endMethod, IAsyncResult asyncResult, Boolean requiresSynchronization)<---
            //]
        }

    }
}
