// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma once

int CallCreateNamedPipeTest();
int CallCreatePipeTest();
int CallDirectoryEnumerationTest();
int CallDeleteFileTest();
int CallDeleteFileStdRemoveTest();
int CallDeleteDirectoryTest();
int CallCreateDirectoryTest();
int CallDetouredZwCreateFile();
int CallDetouredZwOpenFile();
int CallDetouredSetFileInformationFileLink();
int CallDetouredSetFileInformationFileLinkEx();
int CallDetouredSetFileInformationByHandle();
int CallDetouredSetFileInformationByHandle_IncorrectFileNameLength();
int CallDetouredGetFinalPathNameByHandle();
int CallProbeForDirectory();
int CallGetAttributeQuestion();
int CallFileAttributeOnFileWithPipeChar();
int CallAccessNetworkDrive();
int CallAccessInvalidFile();
int CallGetAttributeNonExistent();
int CallGetAttributeNonExistentInDepDirectory();
int CallDetouredCreateFileWWithGenericAllAccess();
int CallDetouredMoveFileExWForRenamingDirectory();
int CallDetouredSetFileInformationByHandleForRenamingDirectory();
int CallDetouredZwSetFileInformationByHandleForRenamingDirectory();
int CallDetouredSetFileDispositionByHandle();
int CallDetouredSetFileDispositionByHandleEx();
int CallDetouredZwSetFileDispositionByHandle();
int CallDetouredZwSetFileDispositionByHandleEx();
int CallDetouredCreateFileWWrite();
int CallCreateFileWithZeroAccessOnDirectory();
int CallCreateFileOnNtEscapedPath();
int CallOpenFileById();
int CallDeleteWithoutSharing();
int CallDeleteOnOpenedHardlink();
int CallCreateSelfForWrite();
int CallMoveFileExWWithTrailingBackSlashNtObject();
int CallMoveFileExWWithTrailingBackSlashNtEscape();
