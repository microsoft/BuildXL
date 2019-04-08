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

EventProperties *CreateEvent(const char *name)
{
    return new EventProperties(name);
}

void DisposeEvent(EventProperties *event)
{
    if (event != nullptr)
    {
        delete event;
        event = nullptr;
    }
}

void SetStringProperty(EventProperties *event, const char *name, const char *value)
{
    if (event != nullptr)
    {
        event->SetProperty(name, value);
    }
}

void SetStringPropertyWithPiiKind(EventProperties *event, const char *name, const char *value, int kind)
{
    if (event != nullptr)
    {
        event->SetProperty(name, value, static_cast<PiiKind>(kind));
    }
}

void SetInt64Property(EventProperties *event, const char *name, const int64_t value)
{
    if (event != nullptr)
    {
        event->SetProperty(name, value);
    }
}

void LogEvent(const AriaLogger *logger, const EventProperties *event)
{
    if (logger != nullptr && event != nullptr)
    {
        ILogger *log = logger->GetLogger();
        log->LogEvent(*event);
    }
}

#endif
