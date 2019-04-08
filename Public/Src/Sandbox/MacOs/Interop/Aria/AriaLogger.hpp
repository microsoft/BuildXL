// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef AriaLogger_
#define AriaLogger_

#pragma GCC visibility push(default)

#ifdef MICROSOFT_INTERNAL // Only needed for internal builds

#include "ILogger.hpp"
#include "LogManager.hpp"

using namespace Microsoft::Applications::Telemetry;

class AriaLogger
{

private:

    std::string token_;
    std::string dbPath_;

    LogConfiguration config_;
    LogManager *logManager_;

public:

    AriaLogger() = delete;
    AriaLogger(const char* token, const char *dbPath);

    ~AriaLogger();

    ILogger *GetLogger() const;
};

extern "C"
{
    extern __cdecl AriaLogger* CreateAriaLogger(const char *, const char *);
    extern __cdecl void DisposeAriaLogger(const AriaLogger *);

    extern __cdecl EventProperties *CreateEvent(const char *);
    extern __cdecl void DisposeEvent(EventProperties *);

    extern __cdecl void SetStringProperty(EventProperties *, const char *, const char *);
    extern __cdecl void SetStringPropertyWithPiiKind(EventProperties *, const char *, const char *, int);
    extern __cdecl void SetInt64Property(EventProperties *, const char *, const int64_t);
    extern __cdecl void LogEvent(const AriaLogger *, const EventProperties *);
}

#pragma GCC visibility pop
#endif
#endif
