// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.AdoBuildRunner.Vsts
{
    /// <summary>
    /// Interface that defines the methods that must be implemented by VSTS loggers
    /// </summary>
    public interface ILogger
    {
        /// <nodoc />
        void Info(string infoMessage);

        /// <nodoc />
        void Debug(string debugMessage);

        /// <nodoc />
        void Warning(string warningMessage);

        /// <nodoc />
        void Error(string errorMessage);

        /// <nodoc />
        void Warning(string format, params object[] args);

        /// <nodoc />
        void Error(string format, params object[] args);

        /// <nodoc />
        void Debug(string format, params object[] args);

        /// <nodoc />
        void Warning(Exception ex, string warningMessage);

        /// <nodoc />
        void Error(Exception ex, string errorMessage);
    }
}
