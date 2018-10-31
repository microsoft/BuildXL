// --------------------------------------------------------------------
//
// Copyright (c) Microsoft Corporation.  All rights reserved.
//
// --------------------------------------------------------------------

#pragma once

int CallPipeTest();
int CallDirectoryEnumerationTest();
int CallDeleteFileTest();
int CallDeleteFileStdRemoveTest();
int CallDeleteDirectoryTest();
int CallCreateDirectoryTest();
int CallDetouredZwCreateFile();
int CallDetouredZwOpenFile();
int CallDetouredSetFileInformationFileLink();
int CallDetouredSetFileInformationByHandle();
int CallDetouredGetFinalPathNameByHandle();
int CallProbeForDirectory();
int CallGetAttributeQuestion();
int CallFileAttributeOnFileWithPipeChar();
int CallAccessNetworkDrive();
int CallAccessInvalidFile();
int CallGetAttributeNonExistent();
int CallGetAttributeNonExistentInDepDirectory();
int CallDetouredCreateFileWWithGenericAllAccess();
int CallDetouredCreateFileWForProbingOnly();
int CallDetouredMoveFileExWForRenamingDirectory();
int CallDetouredSetFileInformationByHandleForRenamingDirectory();
int CallDetouredZwSetFileInformationByHandleForRenamingDirectory();
int CallDetouredCreateFileWWrite();
int CallCreateFileWithZeroAccessOnDirectory();
int CallCreateFileOnNtEscapedPath();
int CallOpenFileById();