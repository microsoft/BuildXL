// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "stdafx.h"
#include "StringOperations.h"
#include <cwctype>

#if MAC_OS_LIBRARY
#include <wchar.h>
#include <string.h>
#endif

#if _WIN32
#include <pathcch.h>
#endif

#define _MAX_EXTENDED_DIR_LENGTH (_MAX_EXTENDED_PATH_LENGTH - _MAX_DRIVE - _MAX_FNAME - _MAX_EXT - 4)
#define _MAX_EXTENDED_PATH_LENGTH 32768 // see https://docs.microsoft.com/en-us/cpp/c-runtime-library/path-field-limits?view=vs-2019

// warning C26472: Don't use a static_cast for arithmetic conversions. Use brace initialization, gsl::narrow_cast or gsl::narrow (type.1).
// warning C26493: Don't use C-style casts (type.4).
// warning C26461: The pointer argument 'pBuffer1' for function 'AreBuffersEqual' can be marked as a pointer to const
// warning C26446: Prefer to use gsl::at() instead of unchecked subscript operator (bounds.4).
// warning C26482: Only index into arrays using constant expressions (bounds.2).
// 
#pragma warning( disable : 26472 26493 26461 26446 26482 )

// Magic numbers known to provide good hash distributions.
// See here: http://www.isthe.com/chongo/tech/comp/fnv/

constexpr DWORD Fnv1Prime32 = 16777619;
constexpr DWORD Fnv1Basis32 = static_cast<const unsigned int>(2166136261);

constexpr inline static DWORD _Fold(DWORD hash, BYTE value) noexcept
{
    return (hash * Fnv1Prime32) ^ (DWORD)value;
}

constexpr inline static DWORD Fold(DWORD hash, WORD value) noexcept
{
    return _Fold(_Fold(hash, (BYTE)value), (BYTE)(((WORD)value) >> 8));
}

#pragma warning( push )
#pragma warning( disable : 4100) // 'nBufferLength' : unreferenced formal parameter // in Release builds
DWORD WINAPI NormalizeAndHashPath(
    __in                            PCPathChar pPath,
    __out_ecount(nBufferLength)     PBYTE pBuffer,
    __in                            DWORD nBufferLength) noexcept
{
    assert(pPath != nullptr);
    assert(pBuffer != nullptr);
    assert((pathlen(pPath) + 1)*sizeof(PathChar) == nBufferLength);

    // not the fastest hashing implementation, but gives awesome distribution
    DWORD hash = Fnv1Basis32;
    size_t i;
    for (i = 0; pPath[i]; i++) {
        const PathChar c = NormalizePathChar(pPath[i]);
        ((PPathChar)pBuffer)[i] = c;
        hash = Fold(hash, c);
    }

    ((PPathChar)pBuffer)[i] = 0;
    assert((i + 1)*sizeof(PathChar) == nBufferLength);
    assert(hash == HashPath(pPath, i));
    return hash;
}
#pragma warning( pop )

DWORD WINAPI HashPath(
    __in_ecount(nLength)        PCPathChar pPath,
    __in                        size_t nLength) noexcept
{
    assert(pPath != nullptr);

    // not the fastest hashing implementation, but gives awesome distribution
    DWORD hash = Fnv1Basis32;
    size_t i;
    for (i = 0; i < nLength; i++) {
        const PathChar c = NormalizePathChar(pPath[i]);
        hash = Fold(hash, c);
    }

    return hash;
}

BOOL WINAPI AreBuffersEqual(
    __in_ecount(nBufferLength)    PBYTE pBuffer1,
    __in_ecount(nBufferLength)    PBYTE pBuffer2,
    __in                          DWORD nBufferLength) noexcept
{
    return memcmp(pBuffer1, pBuffer2, nBufferLength) == 0;
}

BOOL WINAPI ArePathsEqual(
    __in_ecount(nLength)        PCPathChar pPath,
    __in_ecount(nLength + 1)    PCPathChar pNormalizedPath,
    __in                        size_t nLength) noexcept
{
    assert(pPath != nullptr);
    assert(pNormalizedPath != nullptr);

    size_t i;
    for (i = 0; i < nLength; i++) {
        const PathChar c = NormalizePathChar(pPath[i]);
        if (c != pNormalizedPath[i]) {
            return false;
        }
    }

    return !pNormalizedPath[i];
}

bool HasPrefix(PCPathChar str, PCPathChar prefix) noexcept
{
    assert(str != nullptr);
    assert(prefix != nullptr);

    for (size_t i = 0;; i++) {
        if (str[i] == 0) {
            return prefix[i] == 0;
        }

        if (prefix[i] == 0) {
            return true;
        }

        if (!IsPathCharEqual(str[i], prefix[i])) {
            return false;
        }
    }
}

#pragma warning(push)
// warning C6387: 'suffix' could be '0'.
#pragma warning( disable : 6387 )
bool HasSuffix(PCPathChar str, size_t str_length, PCPathChar suffix) noexcept
{
    assert(str != nullptr);
    assert(suffix != nullptr);

    const size_t suffix_length = pathlen(suffix);
    if (suffix_length > str_length) {
        return false;
    }

    for (size_t i = 0; i < suffix_length; i++) {
        const PathChar c1 = str[str_length - i - 1];
        const PathChar c2 = suffix[suffix_length - i - 1];
        if (!IsPathCharEqual(c1, c2)) {
            return false;
        }
    }

    return true;
}
#pragma warning(pop)

bool IsPathWithinTree(PCPathChar tree, PCPathChar path) noexcept
{
    assert(tree != nullptr);
    assert(path != nullptr);

    if (tree[0] == L'\0') {
        return true;
    }

    if (!IsDriveBasedAbsolutePath(tree) || !IsDriveBasedAbsolutePath(path)) {
        return false;
    }

    // If the paths identify different drives, then they are disjoint.
    if (!IsPathCharEqual(tree[0], path[0])) {
        return false;
    }

    // Step beyond "X:\" in both paths.  The positions in both paths can differ, in case
    // there are internal duplicate path separators, such as "C:\Windows\\System32".
    // We treat duplicate path separators as single path separators.  For example, we
    // treat "C:\Windows\\System32" as equivalent to "C:\Windows\System32".
    size_t treepos = 3;
    size_t pathpos = 3;

    //
    // Note: It is possible to unroll some of the interactions of the 'for' loops below,
    // so that the path segments of both 'tree' and 'path' are scanned within the same
    // loop.  However, the code for that is a little more complex.  Because the first 'for'
    // loops pull everything into cache, there's no substantial perf gain in that kind of
    // implementation, and there is a complexity cost.  Therefore, I've chosen the simpler
    // implementation, which should be easy to understand and debug.
    //

    for (;;) {
        // At this point in loop, we are positioned at the start of a path element
        // in both 'tree' and 'path'.  In other words, the character immediately
        // prior to 'pos' is a path separator.

        // Ignore redundant path separators in both paths.
        while (tree[treepos] != 0 && IsDirectorySeparator(tree[treepos])) {
            ++treepos;
        }
        while (path[pathpos] != 0 && IsDirectorySeparator(path[pathpos])) {
            ++pathpos;
        }

        // Now the positions should point to the start of the current path element
        // in each path, if any.

        if (tree[treepos] == 0) {
            // There are no more path elements in 'tree'.
            // We now know that 'path' is equal to, or under, 'tree'.
            return true;
        }

        if (path[pathpos] == 0) {
            // The test path ended before the tree path.
            // We now know that 'path' identifies a directory that is *above* tree.
            return false;
        }


        // Find the end of the current path element in 'tree'.
        const size_t treeElementStart = treepos;
        size_t treeElementLength = 0;
        for (;;) {
            if (tree[treepos] == 0) {
                treeElementLength = treepos - treeElementStart;
                break;
            }
            if (IsDirectorySeparator(tree[treepos])) {
                treeElementLength = treepos - treeElementStart;
                ++treepos;
                break;
            }
            ++treepos;
        }

        // Find the end of the current path element in 'path'.
        const size_t pathElementStart = pathpos;
        size_t pathElementLength = 0;
        for (;;) {
            if (path[pathpos] == 0) {
                pathElementLength = pathpos - pathElementStart;
                break;
            }
            if (IsDirectorySeparator(path[pathpos])) {
                pathElementLength = pathpos - pathElementStart;
                ++pathpos;
                break;
            }
            ++pathpos;
        }

        // Are the current path elements equal?
        if (treeElementLength != pathElementLength) {
            return false;
        }

        for (size_t i = 0; i < treeElementLength; i++) {
            const PathChar ct = tree[treeElementStart + i];
            const PathChar cp = path[pathElementStart + i];
            if (!IsPathCharEqual(ct, cp)) {
                return false;
            }
        }

        // Path element looks the same in both.
        // Keep searching.
    }
}

bool StringLooksLikeRCTempFile(PCPathChar str, size_t str_length) noexcept
{
    assert(str != nullptr);

    if (str_length < 9) {
        return false;
    }
    const PathChar c1 = str[str_length - 9];
    if (!IsPathCharEqual(c1, '\\')) {
        return false;
    }
    const PathChar c2 = str[str_length - 8];
    if (!IsPathCharEqual(c2, 'R')) {
        return false;
    }
    const PathChar c3 = str[str_length - 7];
    if (!IsPathCharEqual(c3, 'C') && !IsPathCharEqual(c3, 'D') && !IsPathCharEqual(c3, 'F')) {
        return false;
    }
    const PathChar c4 = str[str_length - 4];
    if (IsPathCharEqual(c4, '.')) {
        // RC's temp files have no extension.
        return false;
    }
    return true;
}

bool StringLooksLikeBuildExeTraceLog(PCPathChar str, size_t str_length) noexcept
{
    // detect filenames of the following form 
    // _buildc_dep_out.pass<NUMBER>

    assert(str != nullptr);

    int trailingDigits = 0;
    for (; str_length > 0 && str[str_length - 1] >= '0' && str[str_length - 1] <= '9'; str_length--) {
        trailingDigits++;
    }

    if (trailingDigits == 0) {
        return false;
    }

    return HasSuffix(str, str_length, BUILD_EXE_TRACE_FILE);
}

bool StringLooksLikeMtTempFile(PCPathChar str, size_t str_length, PCPathChar expected_extension) noexcept
{
    // The file has this format: <pre><uuuu>.TMP, where <pre> can be anything up to 3 characters.
    // The API call being used by the tool is https://docs.microsoft.com/en-us/windows/desktop/api/fileapi/nf-fileapi-gettempfilenamew

    assert(str != nullptr);
    assert(expected_extension != nullptr);

    if (!HasSuffix(str, str_length, expected_extension)) {
        return false;
    }

    // Find last "\".
    size_t beginCharIndex = (size_t) -1;

    for (size_t i = 0; i < str_length; ++i) {
        if (IsPathCharEqual(str[i], '\\')) {
            beginCharIndex = i;
        }
    }

    // Expect to check "\RCX..".
    if (beginCharIndex == (size_t) -1 || beginCharIndex + 3 >= str_length) {
        return false;
    }

    const PathChar c1 = str[beginCharIndex + 1];
    if (!IsPathCharEqual(c1, 'R')) {
        return false;
    }
    
    const PathChar c2 = str[beginCharIndex + 2];
    if (!IsPathCharEqual(c2, 'C')) {
        return false;
    }

    const PathChar c3 = str[beginCharIndex + 3];
    if (!IsPathCharEqual(c3, 'X')) {
        return false;
    }

    return true;
}

size_t FindFinalPathSeparator(PCPathChar const path) noexcept
{
    assert(path != nullptr);

    size_t newTerminatorPosition = 0;
    size_t currentPosition = 0;

    wchar_t current;
    while ((current = path[currentPosition]) != L'\0') {
        if (IsDirectorySeparator(current)) {
            newTerminatorPosition = currentPosition;
        }

        currentPosition++;
    }

    // newTerminatorPosition is now either the position of the last separator, or 0 (entire string).
    return newTerminatorPosition;
}

bool IsPathToNamedStream(PCPathChar const path, size_t pathLength) noexcept {
    assert(path != nullptr);

    size_t segmentLength[3] = {};
    int segment = 0;

    // N.B. We offset i by 1 (loop when i > 0 rather than i >= 0) since size_t is unsigned.
    for (size_t i = pathLength; i > 0; i--) {
        const PathChar c = path[i - 1];
        if (IsDirectorySeparator(c)) {
            break;
        } else if (c == L':') {
            segment++;
            if (segment == 3) {
                // Too many colons.
                return false;
            }
        }
        else {
            segmentLength[segment]++;
        }
    }

    if (segment == 2) {
        // 2:1:0
        return segmentLength[1] > 0 && segmentLength[2] > 0;
    }
    else if (segment == 1) {
        // 1:0
        return segmentLength[0] > 0 && segmentLength[1] > 0;
    }
    else {
        return false;
    }
}

#if _WIN32

size_t GetRootLength(PCPathChar path) noexcept
{
    if (path == nullptr)
    {
        return 0;
    }

    size_t i = 0;
    size_t volumeSeparatorLength = 2;  // Length to the colon "C:"
    size_t uncRootLength = 2;          // Length to the start of the server name "\\"

    const bool extendedSyntax = HasPrefix(path, NT_LONG_PATH_PREFIX) || HasPrefix(path, NT_PATH_PREFIX);
    const bool extendedUncSyntax = HasPrefix(path, LONG_UNC_PATH_PREFIX);
    const size_t pathLength = pathlen(path);

    if (extendedSyntax)
    {
        // Shift the position we look for the root from to account for the extended prefix
        if (extendedUncSyntax)
        {
            // "\\" -> "\\?\UNC\"
            uncRootLength = pathlen(LONG_UNC_PATH_PREFIX);
        }
        else
        {
            // "C:" -> "\\?\C:"
            volumeSeparatorLength += pathlen(NT_LONG_PATH_PREFIX);
        }
    }

    if ((!extendedSyntax || extendedUncSyntax) && pathLength > 0 && IsDirectorySeparator(path[0]))
    {
        // UNC or simple rooted path (e.g. "\foo", NOT "\\?\C:\foo")

        i = 1; //  Drive rooted (\foo) is one character
        if (extendedUncSyntax || (pathLength > 1 && IsDirectorySeparator(path[1])))
        {
            // UNC (\\?\UNC\ or \\), scan past the next two directory separators at most
            // (e.g. to \\?\UNC\Server\Share or \\Server\Share\)
            i = uncRootLength;
            int n = 2;
            while (i < pathLength && (!IsDirectorySeparator(path[i]) || --n > 0))
            {
                ++i;
            }
        }
    }
    else if (pathLength >= volumeSeparatorLength && path[volumeSeparatorLength - 1] == L':')
    {
        // Path is at least longer than where we expect a colon, and has a colon (\\?\A:, A:)
        // If the colon is followed by a directory separator, move past it
        i = volumeSeparatorLength;
        if (pathLength >= volumeSeparatorLength + 1 && IsDirectorySeparator(path[volumeSeparatorLength]))
        {
            ++i;
        }
    }

    return i;
}

bool AreEqualCaseInsensitively(const std::wstring& s1, const std::wstring& s2)
{
    return s1.size() == s2.size() && std::equal(s1.begin(), s1.end(), s2.begin(),
        [](wchar_t a, wchar_t b) {
            return std::towlower(a) == std::towlower(b);
        }
    );
}

PCPathChar GetPathWithoutPrefix(PCPathChar path) noexcept
{
    assert(path != nullptr);

    return HasPrefix(path, NT_LONG_PATH_PREFIX)
        || HasPrefix(path, NT_PATH_PREFIX)
        || HasPrefix(path, LONG_UNC_PATH_PREFIX)
        || HasPrefix(path, L"\\\\.\\")
        ? path + 4
        : path;
}

#pragma warning( push )
// warning C26414: Move, copy, reassign or reset a local smart pointer 'drive' (r.5).
// warning C26485: Expression 'buffer': No array to pointer decay (bounds.3).
#pragma warning( disable : 26414 26485 )
// Returns a collection of all path atoms of the given path
int TryDecomposePath(const std::wstring& path, std::vector<std::wstring>& elements)
{
    auto drive = std::make_unique<wchar_t[]>(_MAX_DRIVE);
    auto directory = std::make_unique<wchar_t[]>(_MAX_EXTENDED_DIR_LENGTH);
    auto file_name = std::make_unique<wchar_t[]>(_MAX_FNAME);
    auto extension = std::make_unique<wchar_t[]>(_MAX_EXT);

    const errno_t err = _wsplitpath_s(
        path.c_str(),
        drive.get(), _MAX_DRIVE,
        directory.get(), _MAX_EXTENDED_DIR_LENGTH,
        file_name.get(), _MAX_FNAME,
        extension.get(), _MAX_EXT);

    if (err != 0)
    {
        return err;
    }

    std::wstring wdrive = drive.get();
    if (wdrive.size() > 0)
    {
        elements.push_back(std::move(wdrive));
    }

    wchar_t* context = nullptr;
    wchar_t* next = wcstok_s(directory.get(), L"\\/", &context);
    while (next)
    {
        std::wstring dirAtom = next;
        if (dirAtom.size() > 0)
        {
            elements.push_back(std::move(next));
        }

        next = wcstok_s(nullptr, L"\\/", &context);
    }

    std::wstring filenameAndExtension = file_name.get();
    filenameAndExtension.append(extension.get());

    if (filenameAndExtension.size() > 0)
    {
        elements.push_back(std::move(filenameAndExtension));
    }

    return 0;
}

std::wstring NormalizePath(const std::wstring& path)
{
    if (GetRootLength(path.c_str()) == 0)
    {
        return std::wstring(path);
    }

    std::wstring normalizedPath;
    if (path.length() < MAX_PATH)
    {
        PathChar buffer[MAX_PATH];

        // Deliberately not using PATHCCH_FORCE_ENABLE_LONG_NAME_PROCESS to align the long-name capability with
        // what the process is capable of natively.
        PathCchCanonicalizeEx(buffer, MAX_PATH, path.c_str(), PATHCCH_ALLOW_LONG_PATHS);
        normalizedPath.assign(buffer);
    }
    else
    {
        auto buffer = std::make_unique<PathChar[]>(PATHCCH_MAX_CCH);

        // Deliberately not using PATHCCH_FORCE_ENABLE_LONG_NAME_PROCESS to align the long-name capability with
        // what the process is capable of natively.
        PathCchCanonicalizeEx(buffer.get(), PATHCCH_MAX_CCH, path.c_str(), PATHCCH_ALLOW_LONG_PATHS);
        normalizedPath.assign(buffer.get());
    }

    return normalizedPath;
}
#pragma warning( pop )

std::wstring PathCombine(const std::wstring& fragment1, const std::wstring& fragment2) noexcept
{
    if (fragment2.size() == 0)
    {
        return fragment1;
    }

    if (fragment1.size() == 0)
    {
        return fragment2;
    }

    if (GetRootLength(fragment2.c_str()) > 0)
    {
        return fragment2;
    }

    const auto ch = fragment1.back();

    return ch != NT_DIRECTORY_SEPARATOR && ch != UNIX_DIRECTORY_SEPARATOR && ch != NT_VOLUME_SEPARATOR
        ? fragment1 + NT_DIRECTORY_SEPARATOR + fragment2
        : fragment1 + fragment2;
}
#endif // _WIN32