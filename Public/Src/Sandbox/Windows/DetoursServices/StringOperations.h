// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma once

#define NT_DIRECTORY_SEPARATOR       L'\\'
#define UNIX_DIRECTORY_SEPARATOR     L'/'
#define PATH_DOT                     L'.'
#define NT_VOLUME_SEPARATOR          L':'

#define NT_LONG_PATH_PREFIX          L"\\\\?\\"
#define NT_PATH_PREFIX               L"\\??\\"
#define LONG_UNC_PATH_PREFIX         L"\\\\?\\UNC\\"

// ----------------------------------------------------------------------------
// TYPE DEFINITIONS
// ----------------------------------------------------------------------------

#if _WIN32

typedef WCHAR PathChar;
#define pathlen wcslen
#define BUILD_EXE_TRACE_FILE L"_buildc_dep_out.pass"

#else

typedef char PathChar;
#define pathlen strlen
#define BUILD_EXE_TRACE_FILE "_buildc_dep_out.pass"

#endif

#if MAC_OS_LIBRARY || MAC_OS_SANDBOX
#include "utf8proc.h"
#endif // MAC_OS_LIBRARY || MAC_OS_SANDBOX

typedef PathChar* PPathChar;
typedef const PathChar* PCPathChar;

// ----------------------------------------------------------------------------
// INLINE FUNCTION DEFINITIONS
// ----------------------------------------------------------------------------

#if _WIN32
extern _locale_t g_invariantLocale;
#elif MAC_OS_LIBRARY
#include <ctype.h>
#include <assert.h>
#include <wctype.h>
#endif // !MAC_OS_LIBRARY

// warning C26481: Don't use pointer arithmetic. Use span instead (bounds.1).
// warning C6011: Dereferencing NULL pointer 'path'.
#pragma warning( disable : 26481 6011 )

/// NormalizePathChar
///
/// Path character normalization maps upper/lower case characters to a normalized representation.
///
/// It converts to uppercase rather than lowercase because it preserves certain
/// characters which cannot be round-trip converted between locales.
///
/// On the managed side, the file access manifest is constructed by P/Invoking to
/// native APIs which also apply this normalization.
///
/// Note that there are no common functions in managed code and in native code that offer
/// identical comparison or normalization functionality. This is due to the possibility of
/// subtle version differences of the localization tables.
///
/// Also note that the underlying file system, hopefully NTFS or something better, tends
/// to have its own localization table, which is not accessible from user code.
///
/// Thus, there is no way to accurately model the case insensitive behavior of the file system.
/// However, what we do here, should be good enough in practice.

inline PathChar NormalizePathChar(PathChar c) noexcept
{
#if _WIN32
    const PathChar pc{ towupper(c) };
    return pc;
#elif __linux__
    return c;
#elif __APPLE__

#if !defined(MAC_OS_LIBRARY)
    return (PathChar)_towupper_l(c, g_invariantLocale);
#elif  MAC_OS_LIBRARY || MAC_OS_SANDBOX
    return utf8proc_toupper(c);
#endif
    
#endif
}

/// IsPathCharEqual
///
/// Doing an ordinal comparison is appropriate for path characters.
inline bool IsPathCharEqual(PathChar c1, PathChar c2) noexcept
{
    return
        c1 == c2 ||
        NormalizePathChar(c1) == NormalizePathChar(c2);
}

/// IsDirectorySeparator
///
/// Checks whether the given character is a directory separator (checking against all platforms).
/// Both platforms' directory separators are invalid characters in the other system's paths.
constexpr bool IsDirectorySeparator(PathChar c) noexcept
{
    return c == NT_DIRECTORY_SEPARATOR || c == UNIX_DIRECTORY_SEPARATOR;
}

constexpr inline bool IsDriveLetter(PathChar c) noexcept
{
    return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
}

inline bool IsDriveBasedAbsolutePath(PCPathChar path) noexcept
{
    if (path != nullptr && path[0] != 0 && IsDriveLetter(path[0]) && path[1] == NT_VOLUME_SEPARATOR && IsDirectorySeparator(path[2]))
    {
        return true;
    }
    return false;
}

// Indicates if the path is prefixed with \??\ or \\?\, both of which escape Win32 -> NT path canonicalization (including applying current working directory).
inline bool IsWin32NtPathName(PCPathChar path) noexcept
{
    assert(path != nullptr);

    return
            (path[0] == L'\\') &&
            ((path[1] == L'\\') || (path[1] == L'?')) &&
            (path[2] == L'?') &&
            (path[3] == L'\\');
}

// Indicates if the given path is of the 'local device' type (prefix \\.\).
inline bool IsLocalDevicePathName(PCPathChar path) noexcept
{
    assert(path != nullptr);

    return
            (path[0] == L'\\') &&
            (path[1] == L'\\') &&
            (path[2] == L'.') &&
            (path[3] == L'\\');
}

// Indicates if the path is an NT object path (prefix \??\)
inline bool IsNtObjectPath(PCPathChar path) noexcept
{
    assert(path != nullptr);

    return
            (path[0] == L'\\') &&
            (path[1] == L'?') &&
            (path[2] == L'?') &&
            (path[3] == L'\\');
}

// Indicates if this is a pipe device.
inline bool IsPipeDevice(PCPathChar path) noexcept
{
    assert(path != nullptr);

    return (IsLocalDevicePathName(path) || IsNtObjectPath(path)) &&
        (path[4] == L'p') &&
        (path[5] == L'i') &&
        (path[6] == L'p') &&
        (path[7] == L'e') &&
        (path[8] == L'\\');
}

// Indicates if this is name of a special device.
inline bool IsSpecialDeviceName(PCPathChar path) noexcept
{
    assert(path != nullptr);

    return
        IsPipeDevice(path);
    // To add more device names add more name checking functions and OR their result here.
}

// Indicates if this is a long UNC path.
inline bool IsUncPathName(PCPathChar path) noexcept
{
    assert(path != nullptr);
    return
        (path[0] == L'\\') &&
        (path[1] == L'\\') &&
        (path[2] == L'?') &&
        (path[3] == L'U') &&
        (path[4] == L'N') &&
        (path[5] == L'C') &&
        (path[6] == L'\\');
}

// ----------------------------------------------------------------------------
// FUNCTION DECLARATIONS
// ----------------------------------------------------------------------------

// HashPath computes a hash code of a string after applying NormalizePathChar to all characters
DWORD WINAPI HashPath(
    __in_ecount(nLength)        PCPathChar pPath,
    __in                        size_t nLength) noexcept;

// NormalizeAndHashPath applies NormalizePathChar to all characters, storing the result in a buffer, and computes a hash code of the path in the same way as HashPath
DWORD WINAPI NormalizeAndHashPath(
    __in                            PCPathChar pPath,
    __out_ecount(nBufferLength)     PBYTE pBuffer,
    __in                            DWORD nBufferLength) noexcept;

// Fast check if two buffers are equal (for use by managed code where memcmp isn't directly available)
BOOL WINAPI AreBuffersEqual(
    __in_ecount(nBufferLength)    PBYTE pBuffer1,
    __in_ecount(nBufferLength)    PBYTE pBuffer2,
    __in                          DWORD nBufferLength) noexcept;

// Check if a path is equal to a normalized path, after applying NormalizePathChar to all characters of the un-normalized path
BOOL WINAPI ArePathsEqual(
    __in_ecount(nLength)        PCPathChar pPath,
    __in_ecount(nLength + 1)    PCPathChar pNormalizedPath,
    __in                        size_t nLength) noexcept;

// HasPrefix and HasSuffix compare using IsPathCharEqual
bool HasPrefix(PCPathChar text, PCPathChar prefix) noexcept;
bool HasSuffix(PCPathChar str, size_t str_length, PCPathChar suffix) noexcept;

// Returns true if 'path' is exactly equal to 'tree' (ignoring case),
// or if 'path' identifies a path within (under) 'tree'.  For example,
// if tree is 'C:\', and path='C:\Windows', then the return value would
// be true.  But if tree is 'C:\Foo', and path is 'C:\Bar', then the
// return value is false.
//
// Both values are required to be absolute paths, except 'tree' may be an empty string
// (in which case any path is considered to be under it).
bool IsPathWithinTree(PCPathChar tree, PCPathChar path) noexcept;

bool StringLooksLikeRCTempFile(PCPathChar str, size_t str_length) noexcept;

bool StringLooksLikeBuildExeTraceLog(PCPathChar str, size_t str_length) noexcept;

bool StringLooksLikeMtTempFile(PCPathChar str, size_t str_length, PCPathChar expected_extension) noexcept;

// Find the index of the final directory separator (possibly zero), or zero if none are found.
size_t FindFinalPathSeparator(PCPathChar const original) noexcept;

// Determines if the given path is to a named stream other than the default data stream. Expects an already-canonicalized path.
//   C:\foo::$DATA (false)
//   C:\foo:name:$DATA (true)
//   C:\foo:name (true)
//   C:\dir:dir\foo (false)
// We split on colons calling each part a 'segment'. We require that the first segment (filename) and second segment (stream name)
// are non-empty in order to specify a named stream (the default stream can be addressed with a double-colon / zero-length segment).
// See https://msdn.microsoft.com/en-us/library/windows/desktop/aa364404%28v=vs.85%29.aspx
bool IsPathToNamedStream(PCPathChar const path, size_t pathLength) noexcept;

// Gets root length of a path.
size_t GetRootLength(PCPathChar path) noexcept;

// Compares two strings in a case-insensitive manner
bool AreEqualCaseInsensitively(const std::basic_string<PathChar>& s1, const std::basic_string<PathChar>& s2);

// Finds value in string in a case-insensitive manner
std::basic_string<PathChar>::const_iterator FindCaseInsensitively(const std::basic_string<PathChar>& string, const std::basic_string<PathChar>& value);

// Converts an argument vector containing the command line into a single string.
std::basic_string<PathChar> GetCommandLineFromArgv(const PathChar * const * argv);

#if _WIN32
// Returns a collection of all path atoms of the given path
int TryDecomposePath(const std::wstring& path, std::vector<std::wstring>& elements);

// Combines two path fragments into a single path separated by a directory separator.
std::wstring PathCombine(const std::wstring& fragment1, const std::wstring& fragment2) noexcept;

// Normalizes path.
// When the path is a relative path, then the path is returned as is.
// When the path is an absolute path, then the normalization uses PathCchCanonicalizeEx with PATHCCH_ALLOW_LONG_PATHS
// to normalize the path.
__declspec( dllexport )
std::wstring NormalizePath(const std::wstring& path);

// Removes NT or local device prefix from path.
__declspec(dllexport)
PCPathChar GetPathWithoutPrefix(PCPathChar path) noexcept;
#endif