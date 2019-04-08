// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Cache.ImplementationSupport
{
    /// <summary>
    /// List of method names from the <see cref="BuildXL.Cache.Interfaces.ICache"/> interface.
    /// </summary>
    /// <remarks>
    /// This should be used by cache implementations that choose to not call the Start/Stop[MethodName]
    /// methods to provide consistent event names.</remarks>
    public static class InterfaceNames
    {
        /// <nodoc/>
        public const string Close = "Close";

        /// <nodoc/>
        public const string EnumerateStrongFingerprints = "EnumerateStrongFingerprints";

        /// <nodoc/>
        public const string GetCacheEntry = "GetCacheEntry";

        /// <nodoc/>
        public const string GetStream = "GetStream";

        /// <nodoc/>
        public const string PinToCas = "PinToCas";

        /// <nodoc/>
        public const string PinToCasMultiple = "PinToCasMultiple";

        /// <nodoc/>
        public const string ProduceFile = "ProduceFile";

        /// <nodoc/>
        public const string GetStatistics = "GetStatistics";

        /// <nodoc/>
        public const string ValidateContent = "ValidateContent";

        /// <nodoc/>
        public const string AddOrGet = "AddOrGet";

        /// <nodoc/>
        public const string AddToCasStream = "AddToCasStream";

        /// <nodoc/>
        public const string AddToCasFilename = "AddToCasFilename";

        /// <nodoc/>
        public const string IncorporateRecords = "IncorporateRecords";

        /// <nodoc/>
        public const string EnumerateSessionFingerprints = "EnumerateSessionFingerprints";

        /// <nodoc/>
        public const string InitializeCache = "InitializeCache";
    }
}
