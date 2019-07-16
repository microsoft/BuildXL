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

struct AriaEventProperty
{
    const char *name;
    const char *value;
    int64_t piiOrLongValue;
};

extern "C"
{
    extern __cdecl AriaLogger* CreateAriaLogger(const char *, const char *);
    extern __cdecl void DisposeAriaLogger(const AriaLogger *);
    extern __cdecl void LogEvent(const AriaLogger *logger,
                                 const char *eventName,
                                 int eventPropertiesLength,
                                 const AriaEventProperty *eventProperties);

}

#pragma GCC visibility pop
#endif
#endif
