// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Ide.LanguageServer.Tracing
{
    // disable warning regarding 'missing XML comments on public API'. We don't need docs for these values
#pragma warning disable 1591

    /// <summary>
    /// Defines event IDs corresponding to events in <see cref="Logger" />
    /// </summary>
    public enum LogEventId : ushort
    {
        None = 0,

        // reserved 21000 .. 21099 for the language server
        LanguageServerStarted = 21001,
        LanguageServerStoped = 21002,

        LogFileLocation = 21003,
        OperationIsTooLong = 21004,
        ConfigurationChanged = 21005,

        CanNotFindSourceFile = 21006,
        NonCriticalInternalIssue = 21007,
        UnhandledInternalError = 21008,

        FindReferencesIsCancelled = 21009,
        LanguageServerClientType = 21010,
        NewFileWasAdded = 21011,
        FileWasRemoved = 21012,
    }
}
