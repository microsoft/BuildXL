// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef AriaLogger_
#define AriaLogger_

#ifdef MICROSOFT_INTERNAL // Only needed for internal builds

#include "LogManager.hpp"

#include <Windows.h>

using namespace MAT;

class AriaLogger
{

private:

    std::string token_;
    std::string dbPath_;

    ILogger *logger_;

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

int WINAPI Get42();

AriaLogger* WINAPI CreateAriaLogger(const char *, const char *);
void WINAPI DisposeAriaLogger(const AriaLogger *);

void WINAPI LogEvent(const AriaLogger *logger, const char *eventName, int eventPropertiesLength, const AriaEventProperty *eventProperties);

#endif
#endif
