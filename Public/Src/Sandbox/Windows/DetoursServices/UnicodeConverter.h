// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma once

#include "buildXL_mem.h"
#include "DebuggingHelpers.h"

// ----------------------------------------------------------------------------
// CLASSES
// ----------------------------------------------------------------------------

class UnicodeConverter
{
private:
    wchar_t *m_str;

public:
    UnicodeConverter(PCSTR s)
    {
        if (!s)
        {
            m_str = NULL;
        }
        else
        {
            int charsRequired = MultiByteToWideChar(CP_ACP, 0, s, -1, NULL, 0);
            if (charsRequired <= 0) {
                PCWSTR errorMsg = L"UnicodeConverter::UnicodeConverter: Failed to convert string:2";
                Dbg(errorMsg);
                HandleDetoursInjectionAndCommunicationErrors(DETOURS_UNICODE_CONVERSION_18, errorMsg, DETOURS_UNICODE_LOG_MESSAGE_18);
            }

            m_str = new wchar_t[(size_t)charsRequired];
            assert(m_str);

            int charsConverted = MultiByteToWideChar(CP_ACP, 0, s, -1, m_str, charsRequired);
            if (charsConverted != charsRequired) {
                PCWSTR errorMsg = L"UnicodeConverter::UnicodeConverter: Failed to convert string:1";
                Dbg(errorMsg);
                HandleDetoursInjectionAndCommunicationErrors(DETOURS_UNICODE_CONVERSION_18, errorMsg, DETOURS_UNICODE_LOG_MESSAGE_18);
            }
        }
    }

private:
    // make copy-safe by explicitly deleting copy constructors
    UnicodeConverter(const UnicodeConverter &);
    UnicodeConverter& operator=(const UnicodeConverter &);

public:

    ~UnicodeConverter()
    {
        delete[] m_str;
    }

    PWSTR GetMutableString()
    {
        return m_str;
    }

    operator PCWSTR()
    {
        return m_str;
    }
};
