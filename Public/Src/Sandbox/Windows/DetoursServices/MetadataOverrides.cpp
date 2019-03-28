// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "stdafx.h"
#include "MetadataOverrides.h"
#include "FileAccessHelpers.h"

// UTC FILETIME for February 2, 2002 2:02:02 AM
// Why this date? It has a lot of 2s in it, and is in the past.
// Since it is fairly uncommon for file times to be more than brief moment in the future (unlucky clock adjustment),
// it is quite possible that there are latent bugs in which tools assume that (current time - file time) is positive.
const FILETIME NewInputTimestamp{ 0x9add0900, 0x1c1ab8d };

static LARGE_INTEGER GetNewInputTimestampAsLargeInteger() {
    LARGE_INTEGER i;
    i.LowPart = NewInputTimestamp.dwLowDateTime;
    i.HighPart = static_cast<LONG>(NewInputTimestamp.dwHighDateTime);
    return i;
}

void OverrideTimestampsForInputFile(FILE_BASIC_INFO* result) {
    LARGE_INTEGER newTimestamp = GetNewInputTimestampAsLargeInteger();

	if (NormalizeReadTimestamps())
	{
		result->CreationTime = newTimestamp;
		result->LastAccessTime = newTimestamp;
		result->LastWriteTime = newTimestamp;
		result->ChangeTime = newTimestamp;
	}
	else
	{
		if (result->CreationTime.QuadPart < newTimestamp.QuadPart)
		{
			result->CreationTime = newTimestamp;
		}
		if (result->LastAccessTime.QuadPart < newTimestamp.QuadPart)
		{
			result->LastAccessTime = newTimestamp;
		}
		if (result->LastWriteTime.QuadPart < newTimestamp.QuadPart)
		{
			result->LastWriteTime = newTimestamp;
		}
		if (result->ChangeTime.QuadPart < newTimestamp.QuadPart)
		{
			result->ChangeTime = newTimestamp;
		}
	}
}

void ScrubShortFileName(WIN32_FIND_DATAW* result) {
    ZeroMemory(&(result->cAlternateFileName[0]), sizeof(result->cAlternateFileName));
}
