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
    }
}
