// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma once

#include "stdafx.h"
#include "StringOperations.h"

#if !(MAC_OS_SANDBOX)
#include <string>
#include "DebuggingHelpers.h"
#endif // !(MAC_OS_SANDBOX)

#define NoUsn -1

// ----------------------------------------------------------------------------
// ENUMS
// ----------------------------------------------------------------------------

//
// Higher-order macro that enumerates all FileAccessManifestFlag name/value pairs.
// Accepts an arbitrary macro 'm' to which it passes each of the enumerated name/value
// pairs.  This macro should be used to generate the actual enum definition, as well as
// for any other utility methods that should uniformly apply to all enum flags.
//
// IMPORTANT: Keep this in sync with the C# version declared in FileAccessManifest.cs
//
#define FOR_ALL_FAM_FLAGS(m) \
    m(None,                               0x0)            \
    m(BreakOnAccessDenied,                0x1)            \
    m(FailUnexpectedFileAccesses,         0x2)            \
    m(DiagnosticMessagesEnabled,          0x4)            \
    m(ReportAllFileAccesses,              0x8)            \
    m(ReportAllFileUnexpectedAccesses,    0x10)           \
    m(MonitorNtCreateFile,                0x20)           \
    m(MonitorChildProcesses,              0x40)           \
    m(IgnoreCodeCoverage,                 0x80)           \
    m(ReportProcessArgs,                  0x100)          \
    m(ForceReadOnlyForRequestedReadWrite, 0x200)          \
    m(IgnoreReparsePoints,                0x400)          \
    m(NormalizeReadTimestamps,            0x800)          \
    m(IgnoreZwRenameFileInformation,      0x1000)         \
    m(IgnoreSetFileInformationByHandle,   0x2000)         \
    m(UseLargeNtClosePreallocatedList,    0x4000)         \
    m(UseExtraThreadToDrainNtClose,       0x8000)         \
    m(DisableDetours,                     0x10000)        \
    m(LogProcessData,                     0x20000)        \
    m(IgnoreGetFinalPathNameByHandle,     0x40000)        \
    m(LogProcessDetouringStatus,          0x80000)        \
    m(HardExitOnErrorInDetours,           0x100000)       \
    m(CheckDetoursMessageCount,           0x200000)       \
    m(IgnoreZwOtherFileInformation,       0x400000)       \
    m(MonitorZwCreateOpenQueryFile,       0x800000)       \
    m(IgnoreNonCreateFileReparsePoints,   0x1000000)      \
    m(QBuildIntegrated,                   0x4000000)      \
    m(IgnorePreloadedDlls,                0x8000000)      \
    m(DirectoryCreationAccessEnforcement, 0x10000000)

//
// FileAccessManifestFlag enum definition
//
#define GEN_FAM_FLAG_ENUM_NAME_VALUE(name, value) name = value,
enum class FileAccessManifestFlag {
    FOR_ALL_FAM_FLAGS(GEN_FAM_FLAG_ENUM_NAME_VALUE)
};

DEFINE_ENUM_FLAG_OPERATORS(FileAccessManifestFlag)

//
// Checker function for FileAccessManifestFlag enums.
//
// Each generated function looks like:
//
//   inline book CheckDisableDetours(FileAccessManifestFlag flags) { return (flags & FileAccessManifestFlag::DisableDetours) != FileAccessManifestFlag::None; }
//
#define GEN_FAM_FLAG_CHECKER(flag_name, flag_value) \
  inline bool Check##flag_name(FileAccessManifestFlag flags) { return (flags & FileAccessManifestFlag::flag_name) != FileAccessManifestFlag::None; }
FOR_ALL_FAM_FLAGS(GEN_FAM_FLAG_CHECKER)

inline bool CheckReportAnyAccess(FileAccessManifestFlag flags, bool accessDenied)
{
    return
        CheckReportAllFileAccesses(flags) ||
        (accessDenied && CheckReportAllFileUnexpectedAccesses(flags));
}

//
// Keep this in sync with the C# version declared in FileAccessManifest.cs
//
enum class FileAccessManifestExtraFlag {
    None = 0x0,
};

//
// Keep this in sync with the C# version declared in FileAccessPolicy.cs
//
enum FileAccessPolicy
{
    // Allows a read attempt to succeed if the target file exists.
    FileAccessPolicy_AllowRead = 1,
    // Allows a write attempt to succeed, even if the target file doesn't exist.
    FileAccessPolicy_AllowWrite = 2,
    // Allows a read attempt to succeed if the target file does not exist.
    FileAccessPolicy_AllowReadIfNonExistent = 4,
    // Allows a directory to be created.
    FileAccessPolicy_AllowCreateDirectory = 8,

    // If set, then we will report attempts to access files under this scope that succeed (i.e., path and file present).
    // BuildXL uses this information to discover dynamic dependencies, such as #include-ed files.
    FileAccessPolicy_ReportAccessIfExistent = 0x10,

    /// If set, then we will report the USN just after a file open operation for a particular file, or under a scope
    /// to the access report file.  BuildXL uses this information to make sure that the same file version that's hashed that's actually read by a process.
    FileAccessPolicy_ReportUsnAfterOpen = 0x20,

    // If set, then we will report attempts to access files under this scope that fail due to the path or file being absent.
    // BuildXL uses this information to discover dynamic anti-dependencies, such as those on an #include search path, sneaky loader search paths, etc.
    FileAccessPolicy_ReportAccessIfNonExistent = 0x40,

    // If set, then we will report attempts to enumerate directories under this scope
    // BuildXL uses this information to discover dynamic anti-dependencies/directory enumerations, such as those on an #include search path, sneaky loader search paths, etc.
    FileAccessPolicy_ReportDirectoryEnumerationAccess = 0x80,

    // Allows a symlink creation to succeed.
    FileAccessPolicy_AllowSymlinkCreation = 0x100,

    // Allows the real timestamps for input files to be read under this scope. BuildXL always exposes the same consistent timestamp for input files to consuming pips unless
    // this flag is specified
    FileAccessPolicy_AllowRealInputTimestamps = 0x200,

    // If set, then we will report all attempts to access files under this scope (whether existent or not).
    // BuildXL uses this information to discover dynamic dependencies, such as #include-ed files.
    FileAccessPolicy_ReportAccess = FileAccessPolicy_ReportAccessIfNonExistent | FileAccessPolicy_ReportAccessIfExistent,

    FileAccessPolicy_AllowAll = FileAccessPolicy_AllowRead | FileAccessPolicy_AllowReadIfNonExistent | FileAccessPolicy_AllowWrite | FileAccessPolicy_AllowCreateDirectory,
};

// Keep this in sync with the C# version declared in FileAccessStatus.cs
enum FileAccessStatus
{
    FileAccessStatus_None = 0,
    FileAccessStatus_Allowed = 1,
    FileAccessStatus_Denied = 2,
    FileAccessStatus_CannotDeterminePolicy = 3
};

// Keep this in sync with the C# version declared in ReportType.cs
enum ProcessDetouringStatus
{
    ProcessDetouringStatus_None = 0,
    ProcessDetouringStatus_Starting = 1,
    ProcessDetouringStatus_Created = 2,
    ProcessDetouringStatus_Injecting = 3,
    ProcessDetouringStatus_Resuming = 4,
    ProcessDetouringStatus_Resumed = 5,
    ProcessDetouringStatus_Cleanup = 7,
    ProcessDetouringStatus_Done = 8,
    ProcessDetouringStatus_Max = 9,
};

// Keep this in sync with the C# version declared in ReportType.cs
enum ReportType
{
    ReportType_None = 0,
    ReportType_FileAccess = 1,
    ReportType_WindowsCall = 2,
    ReportType_DebugMessage = 3,
    ReportType_ProcessData = 4,
    ReportType_ProcessDetouringStatus = 5,
    ReportType_Max = 6,
};

// Keep this in sync with the C# version declared in FileAccessManifest.cs
enum FileAccessBucketOffsetFlag
{
    ChainStart = 0x01,
    ChainContinuation = 0x02,
    ChainMask = 0x03
};

// ----------------------------------------------------------------------------
// STRUCTS
// ----------------------------------------------------------------------------

#ifndef _DEBUG
// This is needed here because we need a global DWORD to use to divide by zero.
// Do not include "globals.h" because it depends on "DataTypes.h" and circular dependencies are bad.
extern DWORD g_manifestSize;
#endif

extern unsigned long g_injectionTimeoutInMinutes;

// Generates a uint32_t tag, along with CheckValid() and AssertValid() methods.
//
// In debug builds (when _DEBUG is defined):
//   - Tag value is a sanity check to make sure that we are always looking at a valid record;
//   - CheckValid() checks if the value of the tag is as expected; if it is returns `nullptr`, otherwise returns an error message;
//   - AssertValid() asserts (by calling `assert`) that the tag is valid (i.e., `CheckValid()` returns `nullptr`).
//
// In release builds:
//   - no tag field is generated;
//   - CheckValid() always returns `nullptr`;
//   - AssertValid() is empty.
#ifdef _DEBUG
#define GENERATE_TAG(type_name, tag_value)                          \
    typedef uint32_t TagType;                                       \
    TagType Tag;                                                    \
    inline const char* CheckValid() const {                         \
        return (this->Tag != (uint32_t)tag_value)                   \
             ? "Wrong " #type_name " tag. Expected " #tag_value "." \
             : nullptr;                                             \
    }                                                               \
    inline void AssertValid() const {                               \
         assert(CheckValid() == nullptr);                           \
    }
#else
#define GENERATE_TAG(type_name, tag_value) \
    inline const char* CheckValid() const { return nullptr; } \
    inline void AssertValid() const { }
#endif

// ==========================================================================
// == ManifestDebugFlag
// ==========================================================================
typedef struct ManifestDebugFlag_t
{
    typedef uint32_t    FlagType;
    FlagType            Flag;

    inline const char* CheckValid() const
    {
#ifdef _DEBUG
        if (this->Flag != 0xDB600001)
        {
            return "The manifest blob is not a Debug-type manifest.";
        }
#else
        if (this->Flag != 0xDB600000)
        {
            return "The manifest blob is not a Release-type manifest.";
        }
#endif
        return nullptr;
    }

    inline bool CheckValidityAndHandleInvalid() const
    {
#ifdef _DEBUG
        // 0xDB600001 => "debug 1 (on)"
        assert(this->Flag == 0xDB600001);
        if (this->Flag != 0xDB600001)
        {
            Dbg(L"The manifest blob is not a Debug-type manifest. ManifestDebugFlag is %x", this->Flag);
            wprintf(L"The manifest blob is not a Debug-type manifest. ManifestDebugFlag is %x", this->Flag);
            // If the manifest debug flag doesn't match, just return false, so we continue without detouring processes.
            // We already logged that there is a mismatch. Also the message is logged to the debug output console.
            // And just in case it is also printed to the console.
            return false;
        }
#else
        // 0xDB600000 => "debug 0 (off)"
        if (this->Flag != 0xDB600000)
        {
            Dbg(L"The manifest blob is not a Release-type manifest. ManifestDebugFlag is %x", this->Flag);
            wprintf(L"The manifest blob is not a Release-type manifest. ManifestDebugFlag is %x", this->Flag);
            // If the manifest debug flag doesn't match, just return false, so we continue without detouring processes.
            // We already logged that there is a mismatch. Also the message is logged to the debug output console.
            // And just in case it is also printed to the console.
            // The old crashing code could lead to a undefined behaviour since it is called from the DLL's attach process handler
            // a crash could lead to many (even infinite) attempts to load the DLL.
            return false;
        }
#endif
        return true;
    }

    /// GetSize
    ///
    /// There are no variable-length members, so the length of this struct can be determined using sizeof.
    size_t GetSize() const
    {
        return sizeof(ManifestDebugFlag_t);
    }
} ManifestDebugFlag;
typedef const ManifestDebugFlag * PCManifestDebugFlag;

// ==========================================================================
// == ManifestInjectionTimeout
// ==========================================================================
typedef struct ManifestInjectionTimeout_t
{
    typedef uint32_t    FlagType;
    FlagType            Flags;

    inline const char* CheckValid() const
    {
        if (this->Flags <= 0)
        {
            return "The manifest blob timeout value must be greater than 0";
        }

        return nullptr;
    }

    inline bool CheckValidityAndHandleInvalid() const
    {
        // This should never be 0 or less.
        if (this->Flags <= 0)
        {
#ifdef _DEBUG
            assert(false); // For easy debugging/attaching.
#endif // _DEBUG
            Dbg(L"Error: The manifest blob timeout value (in minutes) is %d. It should be bigger than 0.", this->Flags);
            wprintf(L"Error: The manifest blob timeout value (in minutes) is %d. It should be bigger than 0.", this->Flags);
            // If the manifest debug flag doesn't match, just return false, so we continue without detouring processes.
            // We already logged that there is a mismatch. Also the message is logged to the debug output console.
            // And just in case it is also printed to the console.
            // The old crashing code could lead to a undefined behaviour since it is called from the DLL's attach process handler
            // a crash could lead to many (even infinite) attempts to load the DLL.
            return false;
        }

        return true;
    }

    /// GetSize
    ///
    /// There are no variable-length members, so the length of this struct can be determined using sizeof.
    size_t GetSize() const
    {
        return sizeof(ManifestInjectionTimeout_t);
    }
} ManifestInjectionTimeout;
typedef const ManifestInjectionTimeout * PCManifestInjectionTimeout;

// ==========================================================================
// == ManifestTranslatePathsStrings
// ==========================================================================
typedef struct ManifestTranslatePathsStrings_t
{
    GENERATE_TAG("ManifestTranslatePathsStrings", 0xABCDEF02)

    inline size_t GetSize() const
    {
// This conditional compilation here and in ManifestInternalDetoursErrorNotificationFileString_t are necessary because calling sizeof() on
// an empty struct yields undefined behaviour according to the C99 standard. The optimized code the compiler produces returns 1, which is obviously wrong!
#if (MAC_OS_SANDBOX || MAC_OS_LIBRARY) && !_DEBUG
        return 0;
#else
        return sizeof(ManifestTranslatePathsStrings_t);
#endif
    }
} ManifestTranslatePathsStrings_t;
typedef const ManifestTranslatePathsStrings_t * PManifestTranslatePathsStrings;

// ==========================================================================
// == ManifestInternalDetoursErrorNotificationFileString
// ==========================================================================
typedef struct ManifestInternalDetoursErrorNotificationFileString_t
{
    GENERATE_TAG("ManifestInternalDetoursErrorNotificationFileString", 0xABCDEF03)

    inline size_t GetSize() const
    {
#if (MAC_OS_SANDBOX || MAC_OS_LIBRARY) && !_DEBUG
        return 0;
#else
        return sizeof(ManifestInternalDetoursErrorNotificationFileString_t);
#endif
    }
} ManifestInternalDetoursErrorNotificationFileString_t;
typedef const ManifestInternalDetoursErrorNotificationFileString_t * PManifestInternalDetoursErrorNotificationFileString;

// ==========================================================================
// == ManifestFlags
// ==========================================================================
typedef struct ManifestFlags_t
{
    GENERATE_TAG("ManifestFlags", 0xF1A6B10C);

    typedef uint32_t    FlagsType;
    FlagsType           Flags;

    /// GetSize
    ///
    /// There are no variable-length members, so the length of this struct can be determined using sizeof.
    size_t GetSize() const
    {
        return sizeof(ManifestFlags_t);
    }
} ManifestFlags;
typedef const ManifestFlags * PCManifestFlags;

// ==========================================================================
// == ManifestExtraFlags
// ==========================================================================
typedef struct ManifestExtraFlags_t
{
    GENERATE_TAG("ManifestExtraFlags", 0xF1A6B10D)

    typedef uint32_t    ExtraFlagsType;
    ExtraFlagsType      ExtraFlags;

    /// GetSize
    ///
    /// There are no variable-length members, so the length of this struct can be determined using sizeof.
    size_t GetSize() const
    {
        return sizeof(ManifestExtraFlags_t);
    }
} ManifestExtraFlags;
typedef const ManifestExtraFlags * PCManifestExtraFlags;

// ==========================================================================
// == ManifestPipId
// ==========================================================================
typedef struct ManifestPipId_t
{
    GENERATE_TAG("ManifestPipId", 0xF1A6B10E)

#ifdef _DEBUG
    uint32_t padding; // Padding needed since a struct of int and int64 has extra padding, so the int64 is properly aligned.
#endif

    typedef uint64_t    PipIdType;
    PipIdType           PipId;

    /// GetSize
    ///
    /// There are no variable-length members, so the length of this struct can be determined using sizeof.
    size_t GetSize() const
    {
        return sizeof(ManifestPipId_t);
    }
} ManifestPipId;
typedef const ManifestPipId * PCManifestPipId;

// ==========================================================================
// == ManifestReport
// ==========================================================================
typedef struct ManifestReport_t
{
    GENERATE_TAG("ManifestReport", 0xFEEDF00D)

    typedef uint32_t    SizeType;
    typedef PathChar    ReportPathType;
    typedef int         ReportHandleType32Bit;
    typedef union ReportType_t
    {
        ReportPathType          ReportPath[ANYSIZE_ARRAY];
        ReportHandleType32Bit   ReportHandle32Bit;
    } ReportType;

    SizeType            Size;
    ReportType          Report;

    /// IsReportHandle
    ///
    /// If the bottom bit of the Size is 1, then the next field is an integer
    /// representing the handle to the report file.
    /// Otherwise, the next field is a path to a report file.
    bool IsReportHandle() const
    {
        return (Size & 0x1) == 1;
    }

    /// IsReportPresent
    ///
    /// If the size is nonzero then the report is present, otherwise we have an empty report line.
    bool IsReportPresent() const
    {
        return Size > 0;
    }

    /// GetSize
    ///
    /// Calculate the size of this structure by fields which exist for this struct (excluding the union),
    /// and if the report is present, mask out the lowest bit of the size to find out how large the union was.
    size_t GetSize() const
    {
        size_t size = 0;

#ifdef _DEBUG
        size += sizeof(TagType);
#endif

        size += sizeof(SizeType);
        size += static_cast<size_t>(Size & ~0x1); // mask out low-order bit to get the actual size of the next field

        return size;
    }
} ManifestReport;
typedef const ManifestReport * PCManifestReport;

// ==========================================================================
// == ManifestDllBlock
// ==========================================================================
typedef struct ManifestDllBlock_t
{
    GENERATE_TAG("ManifestDllBlock", 0xD11B10CC)

    typedef uint32_t    OffsetType;
    typedef CHAR        DllStringType; // $Note(bxl-team): cannot be WCHAR because IMAGE_EXPORT_DIRECTORY used by detours only supports ASCII
    typedef const DllStringType *PCDllStringType;

    OffsetType          StringBlockSize;
    OffsetType          StringCount;
    OffsetType          DllOffsets[ANYSIZE_ARRAY];
    //The strings follow the table of offsets
    //DllStringType       StringBlock[ANYSIZE_ARRAY];

    /// GetDllString
    ///
    /// Calculate the location of the dll string at index and return that string.
    PCDllStringType GetDllString(size_t index) const
    {
        assert(index < StringCount);
        PCDllStringType stringBlock = reinterpret_cast<PCDllStringType>(DllOffsets + StringCount);
        return &stringBlock[DllOffsets[index]];
    }

    /// GetSize
    ///
    /// Calculate the size of this structure by fields which exist for this struct, and the total
    /// size of the StringBlock (in StringBlockSize).
    size_t GetSize() const
    {
        size_t size = 0;

#ifdef _DEBUG
        size += sizeof(TagType);
#endif
        // Two count values + variable number of offsets
        size += sizeof(OffsetType) * (2 + StringCount);
        size += StringBlockSize;

        return size;
    }
} ManifestDllBlock;
typedef const ManifestDllBlock * PCManifestDllBlock;

// ==========================================================================
// == ManifestSubstituteProcessExecutionShim
// ==========================================================================
typedef struct ManifestSubstituteProcessExecutionShim_t
{
    GENERATE_TAG("ManifestSubstituteProcessExecutionShim", 0xABCDEF04)

    // When nonzero and process substitution is active, determines whether
    // all processes are shimmed except any in the ShimProcessMatch entries,
    // or whether to shim all except the matches.
    uint32_t ShimAllProcesses;

    // Followed by WriteChars string and a custom collection consisting of N
    // entries where each entry is 2 WriteChars strings.

    /// GetSize
    ///
    /// Calculate the size of this structure by fields which exist for this struct, and the total
    /// size of the StringBlock (in StringBlockSize).
    size_t GetSize() const
    {
        return sizeof(ManifestSubstituteProcessExecutionShim_t);
    }
} ManifestSubstituteProcessExecutionShim_t;
typedef const ManifestSubstituteProcessExecutionShim_t * PCManifestSubstituteProcessExecutionShim;

// ==========================================================================
// == ManifestRecord
// ==========================================================================
typedef struct ManifestRecord_t
{
    typedef const ManifestRecord_t * PCManifestRecord; // typedef in inner scope for expressive power

    GENERATE_TAG("ManifestRecord", 0xF00DCAFE)

    typedef uint32_t    HashType;
    typedef uint32_t    PolicyType;
    typedef uint32_t    PathIdType;
    typedef uint32_t    ExpectedUsnPartType;
    typedef uint32_t    BucketCountType;
    typedef uint32_t    ChildOffsetType;
    typedef PCPathChar  PartialPathType;

    HashType            Hash;
    PolicyType          ConePolicy;
    PolicyType          NodePolicy;
    PathIdType          PathId;
    ExpectedUsnPartType ExpectedUsnLo, ExpectedUsnHi; // we split this value up as we don't want to introduce 64-bit alignment here (USN is a 64-bit integer)
    BucketCountType     BucketCount;
    ChildOffsetType     Buckets[ANYSIZE_ARRAY];
    // PartialPathType PartialPath (after the end of the Buckets array)

    inline USN GetExpectedUsn() const {
        return (((USN)this->ExpectedUsnHi) << 32) | this->ExpectedUsnLo;
    }

    inline DWORD GetPathId() const {
        return static_cast<DWORD>(this->PathId);
    }

    inline FileAccessPolicy GetConePolicy() const {
        return static_cast<FileAccessPolicy>(this->ConePolicy);
    }

    // If a specific policy was set for this node, leaving its underlying scope explicitly out, that one is returned. Otherwise, the regular scope
    // policy also applies for this node
    inline FileAccessPolicy GetNodePolicy() const {
        return static_cast<FileAccessPolicy>(this->NodePolicy);
    }

    PCManifestRecord GetChildRecord(BucketCountType index) const
    {
        assert(index < this->BucketCount);

        ChildOffsetType childOffset = this->Buckets[index];
        if (childOffset == 0)
        {
            return nullptr;
        }

        PCManifestRecord childRecord = reinterpret_cast<PCManifestRecord>(reinterpret_cast<const BYTE *>(this) + (childOffset & ~FileAccessBucketOffsetFlag::ChainMask));
        childRecord->AssertValid();

        return childRecord;
    }

    bool IsCollisionChainStart(BucketCountType index) const
    {
        assert(index < this->BucketCount);

        ChildOffsetType childOffset = this->Buckets[index];
        return (childOffset & FileAccessBucketOffsetFlag::ChainStart) != 0;
    }

    bool IsCollisionChainContinuation(BucketCountType index) const
    {
        assert(index < this->BucketCount);

        ChildOffsetType childOffset = this->Buckets[index];
        return (childOffset & FileAccessBucketOffsetFlag::ChainContinuation) != 0;
    }

    PartialPathType GetPartialPath() const
    {
        BucketCountType numBuckets = this->BucketCount;
        PartialPathType path = reinterpret_cast<PartialPathType>(&(this->Buckets[numBuckets]));

        return path;
    }

    __success(return)
    bool FindChild(
        __in  PCPathChar target,
        __in  size_t targetLength,
        __out PCManifestRecord& child) const;
} ManifestRecord;
typedef const ManifestRecord * PCManifestRecord; // duplicated for use in scopes outside of the struct

// ==========================================================================
// == SpecialProcessKind
// ==========================================================================
// Characterization of the currently running process
// These are special processes for which we remove some artificial file accesses from reporting.
// We should not detour anything if the process is WinDbg.
enum SpecialProcessKind {
    NotSpecial,
    WinDbg,
    RC,
    CCCheck,
    CCRewrite,
    CCRefGen,
    CCDocGen,
    Csc,
    Cvtres,
    Resonexe,
    Mt
};
