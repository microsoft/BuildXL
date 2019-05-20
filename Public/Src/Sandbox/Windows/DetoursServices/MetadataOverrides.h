// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

// Functions for overriding file metadata based on policy.
// For example, timestamps may be forced to a known (deterministic) value for input (read-only) files.

extern const FILETIME NewInputTimestamp;

// Replaces timestamps to be NewInputTimestamp.
// This implementation works for types with ftCreationTime, ftLastAccessTime, and ftLastWriteTime.
// This includes BY_HANDLE_FILE_INFORMATION, WIN32_FILE_ATTRIBUTE_DATA, WIN32_FIND_DATAA, and WIN32_FIND_DATAW
template<typename TResult>
void OverrideTimestampsForInputFile(TResult* result) {
    static_assert(std::is_same<decltype(result->ftCreationTime), FILETIME>::value, "result->ftCreationTime must be a FILETIME");
    static_assert(std::is_same<decltype(result->ftLastAccessTime), FILETIME>::value, "result->ftLastAccessTime must be a FILETIME");
    static_assert(std::is_same<decltype(result->ftLastWriteTime), FILETIME>::value, "result->ftLastWriteTime must be a FILETIME");
    
    if (NormalizeReadTimestamps())
    {
        result->ftCreationTime = NewInputTimestamp;
        result->ftLastAccessTime = NewInputTimestamp;
        result->ftLastWriteTime = NewInputTimestamp;
    }
    else
    {
        if (CompareFileTime(&(result->ftCreationTime), &NewInputTimestamp) == -1)
        {
            result->ftCreationTime = NewInputTimestamp;
        }
        if (CompareFileTime(&(result->ftLastAccessTime), &NewInputTimestamp) == -1)
        {
            result->ftLastAccessTime = NewInputTimestamp;
        }
        if (CompareFileTime(&(result->ftLastWriteTime), &NewInputTimestamp) == -1)
        {
            result->ftLastWriteTime = NewInputTimestamp;
        }
    }
}

void OverrideTimestampsForInputFile(FILE_BASIC_INFO* result);

// Removes the short file name from directory-entry data (simulate short file names disabled on the volume).
// TODO: Could scrub FILE_ID_BOTH_DIR_INFO too (https://msdn.microsoft.com/en-us/library/windows/desktop/aa364226(v=vs.85).aspx)
void ScrubShortFileName(WIN32_FIND_DATAW* result);
