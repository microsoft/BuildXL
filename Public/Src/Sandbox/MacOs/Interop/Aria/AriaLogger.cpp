// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "AriaLogger.hpp"

#ifdef MICROSOFT_INTERNAL // Only needed for internal builds

#pragma mark Aria logger class definition

AriaLogger::AriaLogger(const char* token, const char *dbPath)
{
    token_ = token;
    dbPath_ = dbPath;

    config_.minimumTraceLevel = ACTTraceLevel_None; // Usefull for debugging
    config_.cacheFileSizeLimitInBytes = 1024 * 1024 * 64; // 64 MB
    config_.maxTeardownUploadTimeInSec = 5;
    config_.cacheFilePath = dbPath_;

    logManager_ = dynamic_cast<LogManager *>(LogManager::Initialize(token, config_));

    // We use this on full sized build machines only
    logManager_->SetTransmitProfile(TransmitProfile_RealTime);
}

AriaLogger::~AriaLogger()
{
    logManager_->FlushAndTeardown();
}

ILogger *AriaLogger::GetLogger() const
{
    return logManager_->GetLogger(token_);
};

#pragma mark External Interface

AriaLogger* CreateAriaLogger(const char *token, const char *dbPath)
{
    return new AriaLogger(token, dbPath);
}

void DisposeAriaLogger(const AriaLogger *logger)
{
    if (logger != nullptr)
    {
        delete logger;
        logger = nullptr;
    }
}

void LogEvent(const AriaLogger *logger,
              const char *eventName,
              int eventPropertiesLength,
              const AriaEventProperty *eventProperties)
{
    if (logger != nullptr)
    {
        EventProperties props(eventName);
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
