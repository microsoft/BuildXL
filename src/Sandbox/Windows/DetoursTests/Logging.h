// --------------------------------------------------------------------
//
// Copyright (c) Microsoft Corporation.  All rights reserved.
//
// --------------------------------------------------------------------

#pragma once

int CreateProcessWLogging(void);
int CreateProcessALogging(void);
int CreateFileWLogging(void);
int CreateFileALogging(void);
int GetVolumePathNameWLogging(void);
int GetFileAttributesALogging(void);
int GetFileAttributesWLogging(void);
int GetFileAttributesExWLogging(void);
int GetFileAttributesExALogging(void);

int CopyFileWLogging(void);
int CopyFileALogging(void);
int CopyFileExWLogging(void);
int CopyFileExALogging(void);
int MoveFileWLogging(void);
int MoveFileALogging(void);
int MoveFileExWLogging(void);
int MoveFileExALogging(void);
int MoveFileWithProgressWLogging(void);
int MoveFileWithProgressALogging(void);
int ReplaceFileWLogging(void);
int ReplaceFileALogging(void);
int DeleteFileWLogging(void);
int DeleteFileALogging(void);

int CreateHardLinkWLogging(void);
int CreateHardLinkALogging(void);
int CreateSymbolicLinkWLogging(void);
int CreateSymbolicLinkALogging(void);
int FindFirstFileWLogging(void);
int FindFirstFileALogging(void);
int FindFirstFileExWLogging(void);
int FindFirstFileExALogging(void);
int GetFileInformationByHandleExLogging(void);
int SetFileInformationByHandleLogging(void);
int OpenFileMappingWLogging(void);
int OpenFileMappingALogging(void);
int GetTempFileNameWLogging(void);
int GetTempFileNameALogging(void);
int CreateDirectoryWLogging(void);
int CreateDirectoryALogging(void);
int CreateDirectoryExWLogging(void);
int CreateDirectoryExALogging(void);
int RemoveDirectoryWLogging(void);
int RemoveDirectoryALogging(void);
int DecryptFileWLogging(void);
int DecryptFileALogging(void);
int EncryptFileWLogging(void);
int EncryptFileALogging(void);
int OpenEncryptedFileRawWLogging(void);
int OpenEncryptedFileRawALogging(void);
int OpenFileByIdLogging(void);
