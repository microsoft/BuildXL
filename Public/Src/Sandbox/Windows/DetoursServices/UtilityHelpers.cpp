// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "UtilityHelpers.h"
#include "CanonicalizedPath.h"
#include "DetoursHelpers.h"

bool TryFindImage(_In_ std::wstring candidatePath, _Out_opt_ std::wstring& imageName)
{
    if (IsPathToImage(candidatePath, imageName))
    {
        return true;
    }

    if (candidatePath.size() > 4 && candidatePath.substr(candidatePath.size() - 4, 4) != L".exe")
    {
        std::wstring candidatePathExe(candidatePath);
        if (IsPathToImage(candidatePathExe.append(L".exe"), imageName))
        {
            return true;
        }
    }

    return false;
}

bool IsPathToImage(_In_ std::wstring candidatePath, _Out_opt_ std::wstring& imageName)
{
    CanonicalizedPath path = CanonicalizedPath::Canonicalize(candidatePath.c_str());
    if (path.IsNull())
    {
        imageName.assign(L"");
        return true;
    }

    if (ExistsAsFile(path.GetPathString()))
    {
        imageName.assign(path.GetLastComponent());
        return true;
    }

    return false;
}

std::wstring GetImageName(_In_opt_ LPCWSTR lpApplicationName, _In_opt_ LPWSTR lpCommandLine)
{
    // If the application name is not null, it should be a path to the image name
    if (lpApplicationName != nullptr)
    {
        CanonicalizedPath path = CanonicalizedPath::Canonicalize(lpApplicationName);
        if (!path.IsNull())
        {
            return std::wstring(path.GetLastComponent());
        }

        // If the path could not be parsed, the process is bound to fail anyway
        return L"";
    }
    else
    {
        if (lpCommandLine == nullptr)
        {
            // the command line should not be null
            return L"";
        }

        std::wstring imageNameCandidate = L"";

        LPWSTR cursor = lpCommandLine;
        unsigned int count = 0;
        // First check for a leading quote
        if (*cursor == L'\"')
        {
            cursor++;
            lpCommandLine = cursor;
            while (*cursor) {
                if (*cursor == (WCHAR)'\"') {
                    break;
                }
                cursor++;
                count++;
            }
            // Start with the first quoted string
            imageNameCandidate.assign(lpCommandLine, count);
            
            // If we found and ending quote, advance the cursor past it
            if (*cursor == (WCHAR)'\"')
            {
                cursor++;
            }
        }
        else
        {
            // Look for the first whitespace/tab
            while (*cursor) {
                if ((*cursor == (WCHAR)' ') || (*cursor == (WCHAR)'\t')) {
                    break;
                }
                cursor++;
                count++;
            }
            // Start with the first string delimited with the first space/tab
            imageNameCandidate.assign(lpCommandLine, count);
        }

        std::wstring imageName;
        if (TryFindImage(imageNameCandidate, imageName))
        {
            return imageName;
        }

        // Now keep adding space/tab separated blocks until we find an image or run out of command line
        while (*cursor)
        {
            lpCommandLine = cursor;
            count = 0;
            // skip trailing spaces
            while ((*cursor == (WCHAR)' ') || (*cursor == (WCHAR)'\t'))
            {
                count++;
                cursor++;
            }
            // Move through the next space separated block
            while ((*cursor) && (*cursor != (WCHAR)' ') && (*cursor != (WCHAR)'\t'))
            {
                count++;
                cursor++;
            }
            imageNameCandidate.append(lpCommandLine, count);
            if (TryFindImage(imageNameCandidate, imageName))
            {
                return imageName;
            }
        }

        return L"";
    }
}