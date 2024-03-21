// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma once

// Data structures - from ntifs.h
typedef struct _REPARSE_DATA_BUFFER {
    ULONG  ReparseTag;
    USHORT ReparseDataLength;
    USHORT Reserved;
    union {
        struct {
            USHORT SubstituteNameOffset;
            USHORT SubstituteNameLength;
            USHORT PrintNameOffset;
            USHORT PrintNameLength;
            ULONG  Flags;
            WCHAR  PathBuffer[1];
        } SymbolicLinkReparseBuffer;
        struct {
            USHORT SubstituteNameOffset;
            USHORT SubstituteNameLength;
            USHORT PrintNameOffset;
            USHORT PrintNameLength;
            WCHAR  PathBuffer[1];
        } MountPointReparseBuffer;
        struct {
            UCHAR DataBuffer[1];
        } GenericReparseBuffer;
    };
} REPARSE_DATA_BUFFER, * PREPARSE_DATA_BUFFER;


int CallDetouredFileCreateWithSymlink();
int CallDetouredFileCreateWithNoSymlink();
int CallDetouredProcessCreateWithDirectorySymlink();
int CallDetouredProcessCreateWithSymlink();
int CallCreateSymLinkOnFiles();
int CallCreateSymLinkOnDirectories();
int CallAccessSymLinkOnFiles();
int CallCreateAndDeleteSymLinkOnFiles();
int CallMoveSymLinkOnFilesNotEnforceChainSymLinkAccesses();
int CallAccessSymLinkOnDirectories();
int CallDetouredFileCreateThatAccessesChainOfSymlinks();
int CallDetouredFileCreateThatDoesNotAccessChainOfSymlinks();
int CallDetouredCopyFileFollowingChainOfSymlinks();
int CallDetouredCopyFileNotFollowingChainOfSymlinks();
int CallDetouredNtCreateFileThatAccessesChainOfSymlinks();
int CallDetouredNtCreateFileThatDoesNotAccessChainOfSymlinks();
int CallAccessNestedSiblingSymLinkOnFiles();
int CallAccessJunctionSymlink_Real();
int CallAccessJunctionSymlink_Junction();
int CallAccessOnChainOfJunctions();
int CallDetouredAccessesCreateSymlinkForQBuild();
int CallDetouredCreateFileWForSymlinkProbeOnlyWithReparsePointFlag();
int CallDetouredCreateFileWForSymlinkProbeOnlyWithoutReparsePointFlag();
int CallDetouredCopyFileToExistingSymlinkFollowChainOfSymlinks();
int CallDetouredCopyFileToExistingSymlinkNotFollowChainOfSymlinks();
int CallProbeDirectorySymlink();
int CallProbeDirectorySymlinkTargetWithReparsePointFlag();
int CallProbeDirectorySymlinkTargetWithoutReparsePointFlag();
int CallValidateFileSymlinkAccesses();
int CallOpenFileThroughMultipleDirectorySymlinks();
int CallOpenFileThroughDirectorySymlinksSelectivelyEnforce();
int CallModifyDirectorySymlinkThroughDifferentPathIgnoreFullyResolve();
int CallDeleteSymlinkUnderDirectorySymlinkWithFullSymlinkResolution();
int CallOpenNonExistentFileThroughDirectorySymlink();
int CallNtOpenNonExistentFileThroughDirectorySymlink();
int CallDirectoryEnumerationThroughDirectorySymlink();
int CallDeviceIOControlGetReparsePoint();
int CallDeviceIOControlSetReparsePoint();
