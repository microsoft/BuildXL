// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "AriaLogger.hpp"

#ifdef MICROSOFT_INTERNAL // Only needed for internal builds

 LOGMANAGER_INSTANCE

//// Aria logger class definition

AriaLogger::AriaLogger(const char* token, const char *dbPath, int teardownTimeoutInSeconds)
{
    token_ = token;
    dbPath_ = dbPath;

    auto& config = LogManager::GetLogConfiguration();
    config[CFG_INT_MAX_TEARDOWN_TIME] = teardownTimeoutInSeconds;
    // config[CFG_STR_CACHE_FILE_PATH] = dbPath; // not necessary

    logger_ = LogManager::Initialize(token);
    LogManager::SetTransmitProfile(TransmitProfile_NearRealTime);
}

#pragma warning( push )
// If you define or delete any default operation in the type 'class AriaLogger', define or delete them all
// Complaining about not declaring copy/move constructors/destructors, should be fine to ignore for this class.
#pragma warning( disable : 26432 )
AriaLogger::~AriaLogger()
{
#pragma warning( push )
// The function is declared 'noexcept' but calls function 'FlushAndTeardown()' which may throw exceptions
// This destructor is not declared as noexcept, not sure why we get warning, but we can ignore it.
#pragma warning( disable : 26447 )
    LogManager::FlushAndTeardown();
#pragma warning( pop )
}
#pragma warning( pop )

ILogger *AriaLogger::GetLogger() const noexcept
{
    return logger_;
};

//// External Interface
#pragma warning( push )
// Avoid calling new and delete explicitly, use std::make_unique<T> instead
// This interface is only called by the managed side, so we can ignore this warning
#pragma warning( disable : 26409 )
AriaLogger* WINAPI CreateAriaLogger(const char *token, const char *dbPath, int teardownTimeoutInSeconds)
{
    return new AriaLogger(token, dbPath, teardownTimeoutInSeconds);
}
#pragma warning( pop )

void WINAPI DisposeAriaLogger(const AriaLogger *logger) noexcept
{
    if (logger != nullptr)
    {
        delete logger;
    }
}

void WINAPI LogEvent(const AriaLogger *logger, const char *eventName, int eventPropertiesLength, const AriaEventProperty *eventProperties)
{
    if (logger != nullptr && eventProperties != nullptr)
    {
        EventProperties props;
        props.SetName(eventName);
        for (int i = 0; i < eventPropertiesLength; i++)
        {
#pragma warning( push )
// Don't use pointer arithmetic. Use span instead
// No need to use spans for these since they are just being passed into props
#pragma warning( disable : 26481 )
            const char *propName = eventProperties[i].name;
            const char *propValue = eventProperties[i].value;
            const int64_t piiOrValue = eventProperties[i].piiOrLongValue;
#pragma warning( pop )

            if (propValue == nullptr)
            {
                props.SetProperty(propName, piiOrValue);
            }
            else if (piiOrValue == (int)PiiKind::PiiKind_None)
            {
                props.SetProperty(propName, propValue);
            }
            else
            {
                props.SetProperty(propName, propValue, static_cast<PiiKind>(piiOrValue));
            }
        }

        ILogger *log = logger->GetLogger();
        if (log != nullptr)
        {
            log->LogEvent(props);
        }
    }
}

#endif
