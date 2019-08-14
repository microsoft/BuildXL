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

AriaLogger::~AriaLogger()
{
    LogManager::FlushAndTeardown();
}

ILogger *AriaLogger::GetLogger() const
{
    return logger_;
};

//// External Interface

AriaLogger* WINAPI CreateAriaLogger(const char *token, const char *dbPath, int teardownTimeoutInSeconds)
{
    return new AriaLogger(token, dbPath, teardownTimeoutInSeconds);
}

void WINAPI DisposeAriaLogger(const AriaLogger *logger)
{
    if (logger != nullptr)
    {
        delete logger;
    }
}

void WINAPI LogEvent(const AriaLogger *logger, const char *eventName, int eventPropertiesLength, const AriaEventProperty *eventProperties)
{
    if (logger != nullptr)
    {
        EventProperties props;
        props.SetName(eventName);
        for (int i = 0; i < eventPropertiesLength; i++)
        {
            const char *propName = eventProperties[i].name;
            const char *propValue = eventProperties[i].value;
            int64_t piiOrValue = eventProperties[i].piiOrLongValue;

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
        log->LogEvent(props);
    }
}

#endif
