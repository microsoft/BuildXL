//////////////////////////////////////////////////////////////////////////////
//
//  Add DLLs to a module import table (uimports.cpp of detours.lib)
//
//  Microsoft Research Detours Package, Version 3.0 Build_310.
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  Note that this file is included into creatwth.cpp one or more times
//  (once for each supported module format).
//
// BuildXL-specific changes (forked from MSR version):
//  - Support for detouring 32-bit from 64-bit (without UpdImports)
//  - ETW tracing (see tracing.cpp).

// UpdateImports32 aka UpdateImports64
static BOOL UPDATE_IMPORTS_XX(HANDLE hProcess,
                              HMODULE hModule,
                              __in_ecount(nDlls) LPCSTR *plpDlls,
                              DWORD nDlls,
							  PBYTE* pbClr)
{
    BOOL fSucceeded = FALSE;
    BYTE * pbNew = NULL;

    PBYTE pbModule = (PBYTE)hModule;

    IMAGE_DOS_HEADER idh;
    ZeroMemory(&idh, sizeof(idh));
    if (!ReadProcessMemory(hProcess, pbModule, &idh, sizeof(idh), NULL)) {
        DETOUR_TRACE_ERROR(L"ReadProcessMemory(idh@%p..%p) failed: %d\n",
                      pbModule, pbModule + sizeof(idh), GetLastError());

      finish:
        if (pbNew != NULL) {
            delete[] pbNew;
            pbNew = NULL;
        }
        return fSucceeded;
    }

    IMAGE_NT_HEADERS_XX inh;
    ZeroMemory(&inh, sizeof(inh));

    if (!ReadProcessMemory(hProcess, pbModule + idh.e_lfanew, &inh, sizeof(inh), NULL)) {
        DETOUR_TRACE_ERROR(L"ReadProcessMemory(inh@%p..%p) failed: %d\n",
                      pbModule + idh.e_lfanew,
                      pbModule + idh.e_lfanew + sizeof(inh),
                      GetLastError());
        goto finish;
    }

    if (inh.OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR_MAGIC_XX) {
        DETOUR_TRACE_ERROR(
            L"Wrong size image (%04x != %04x) -> ERROR_INVALID_BLOCK\n",
            inh.OptionalHeader.Magic, IMAGE_NT_OPTIONAL_HDR_MAGIC_XX);
        SetLastError(ERROR_INVALID_BLOCK);
        goto finish;
    }

    // Zero out the bound table so loader doesn't use it instead of our new table.
	// When the Windows loader loads a PE file into memory, it examines the list of
	// import descriptors and their associated import address table (IAT) to load 
	// the required DLLs into the process address space. More precisely, the loader
	// overwrites entries of IAT, IMAGE_THUNK_DATA, with the addresses of imported
	// functions. As this step takes time, one can use bind.exe to calculate and
	// bound these addresses apriori. The information that the loader uses to determine
	// if the bound addresses are valid is kept in IMAGE_BOUND_IMPORT_DESCRIPTOR
	// structure, and the address to the first element of array of 
	// IMAGE_BOUND_IMPORT_DESCRIPTOR sturctures is recoreded in 
	// OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_BOUND_IMPORT].
    inh.BOUND_DIRECTORY.VirtualAddress = 0;
    inh.BOUND_DIRECTORY.Size = 0;

    // Find the size of the mapped file.
    DWORD dwFileSize = 0;
    DWORD dwSec = idh.e_lfanew +
        FIELD_OFFSET(IMAGE_NT_HEADERS_XX, OptionalHeader) +
        inh.FileHeader.SizeOfOptionalHeader;

    for (DWORD i = 0; i < inh.FileHeader.NumberOfSections; i++) {
        IMAGE_SECTION_HEADER ish;
        ZeroMemory(&ish, sizeof(ish));

        if (!ReadProcessMemory(hProcess, pbModule + dwSec + sizeof(ish) * i, &ish,
                               sizeof(ish), NULL)) {
            DETOUR_TRACE_ERROR(L"ReadProcessMemory(ish@%p..%p) failed: %d\n",
                          pbModule + dwSec + sizeof(ish) * i,
                          pbModule + dwSec + sizeof(ish) * (i + 1),
                          GetLastError());
            goto finish;
        }

        DETOUR_TRACE(("ish[%d] : va=%08x sr=%d\n", i, ish.VirtualAddress, ish.SizeOfRawData));

        // If the file didn't have an IAT_DIRECTORY, we assign it...
		// It is known that some linkers do not set this directory entry and the application
		// will run nevertheless. The loader only uses this to temporarily mark the IATs
		// as read-write during import resolution, but can resolve the imports without it.
		// It is still unknown why we have to assign IAT_DIRECTORY if we don't have one.
        if (inh.IAT_DIRECTORY.VirtualAddress == 0 &&
            inh.IMPORT_DIRECTORY.VirtualAddress >= ish.VirtualAddress &&
            inh.IMPORT_DIRECTORY.VirtualAddress < ish.VirtualAddress + ish.SizeOfRawData) {

            inh.IAT_DIRECTORY.VirtualAddress = ish.VirtualAddress;
            inh.IAT_DIRECTORY.Size = ish.SizeOfRawData;
        }

        // Find the end of the file...
        if (dwFileSize < ish.PointerToRawData + ish.SizeOfRawData) {
            dwFileSize = ish.PointerToRawData + ish.SizeOfRawData;
        }
    }
    DETOUR_TRACE(("dwFileSize = %08x\n", dwFileSize));

#if IGNORE_CHECKSUMS
    // Find the current checksum.
    WORD wBefore = ComputeChkSum(hProcess, pbModule, &inh);
    DETOUR_TRACE(("ChkSum: %04x + %08x => %08x\n", wBefore, dwFileSize, wBefore + dwFileSize));
#endif

    DETOUR_TRACE(("     Imports: %p..%p\n",
                  (DWORD_PTR)pbModule + inh.IMPORT_DIRECTORY.VirtualAddress,
                  (DWORD_PTR)pbModule + inh.IMPORT_DIRECTORY.VirtualAddress +
                  inh.IMPORT_DIRECTORY.Size));

	// We need to move all the IMAGE_IMPORT_DESCRIPTORs (IIDs) to a location where
	// there is a plenty of space. We first have to calculate the amount of space
	// that we need.

	// Space for IIDs of DLLs to be injected.
    DWORD obRem = sizeof(IMAGE_IMPORT_DESCRIPTOR) * nDlls;

	// Space for IIDs of all DLLs (existing and to-be injected ones).
    DWORD obTab = PadToDwordPtr(obRem +
                                inh.IMPORT_DIRECTORY.Size +
                                sizeof(IMAGE_IMPORT_DESCRIPTOR));

	// For XX-bit process, we need 2 * XX-bit space for each to-be injected DLL
	// for storing IMAGE_ORDINAL_FLAG_XX for the IMAGE_THUNK_DATA_XX.
    DWORD obDll = obTab + sizeof(DWORD_XX) * 4 * nDlls;
    DWORD obStr = obDll;
    DWORD cbNew = obStr;

	// Space for the name of the DLLs that are going to be injected (see Name1 field of IID).
    for (DWORD n = 0; n < nDlls; n++) {
        cbNew += PadToDword((DWORD)strlen(plpDlls[n]) + 1);
    }

	// Allocate in-memory buffer.
    pbNew = new BYTE [cbNew];
    if (pbNew == NULL) {
        DETOUR_TRACE(("new BYTE [cbNew] failed.\n"));
        goto finish;
    }
    ZeroMemory(pbNew, cbNew);

    PBYTE pbBase = pbModule;
    PBYTE pbNext = pbBase
        + inh.OptionalHeader.BaseOfCode
        + inh.OptionalHeader.SizeOfCode
        + inh.OptionalHeader.SizeOfInitializedData
        + inh.OptionalHeader.SizeOfUninitializedData;
    if (pbBase < pbNext) {
        pbBase = pbNext;
    }
    DETOUR_TRACE(("pbBase = %p\n", pbBase));

	// Allocate space in the PE file for moving the IIDs.
	PBYTE pbNewIid = FindAndAllocateNearBase(hProcess, pbBase, cbNew);
    if (pbNewIid == NULL) {
        DETOUR_TRACE(("FindAndAllocateNearBase failed.\n"));
        goto finish;
    }

    DWORD obBase = (DWORD)(pbNewIid - pbModule);
    DWORD dwProtect = 0;
    if (inh.IMPORT_DIRECTORY.VirtualAddress != 0) {
        // Read the old import directory if it exists.
#if 0
        if (!VirtualProtectEx(hProcess,
                              pbModule + inh.IMPORT_DIRECTORY.VirtualAddress,
                              inh.IMPORT_DIRECTORY.Size, PAGE_EXECUTE_READWRITE, &dwProtect)) {
            DETOUR_TRACE_ERROR(L"VirtualProtectEx(import) write failed: %d\n", GetLastError());
            goto finish;
        }
#endif
        DETOUR_TRACE(("IMPORT_DIRECTORY perms=%x\n", dwProtect));

		// Read existing IIDs into the in-memory buffer, but place them past
		// the space for the IIDs of the to-be injected DLLs.
        if (!ReadProcessMemory(hProcess,
                               pbModule + inh.IMPORT_DIRECTORY.VirtualAddress,
                               pbNew + obRem,
                               inh.IMPORT_DIRECTORY.Size, NULL)) {
            DETOUR_TRACE_ERROR(L"ReadProcessMemory(imports) failed: %d\n", GetLastError());
            goto finish;
        }
    }

    PIMAGE_IMPORT_DESCRIPTOR piid = (PIMAGE_IMPORT_DESCRIPTOR)pbNew;
    DWORD_XX *pt;

	// Create an IID for each DLL to be injected.
    for (DWORD n = 0; n < nDlls; n++) {

        if (cbNew < obStr) {
            DETOUR_TRACE(("Integer overflow: %d\n", ERROR_ARITHMETIC_OVERFLOW));
            goto finish;
        }

		// Copy the string name of the DLL to the in-memory buffer.
        HRESULT hrRet = StringCchCopyA((char*)pbNew + obStr, cbNew - obStr, plpDlls[n]);
        if (FAILED(hrRet))
        {
            DETOUR_TRACE_ERROR(L"StringCchCopyA failed: %d\n", GetLastError());
            goto finish;
        }

		// Set values for the IID.
        DWORD nOffset = obTab + (sizeof(DWORD_XX) * (4 * n));
        piid[n].OriginalFirstThunk = obBase + nOffset;
        pt = ((DWORD_XX*)(pbNew + nOffset));
        pt[0] = IMAGE_ORDINAL_FLAG_XX + 1;
        pt[1] = 0;

        nOffset = obTab + (sizeof(DWORD_XX) * ((4 * n) + 2));
        piid[n].FirstThunk = obBase + nOffset;
        pt = ((DWORD_XX*)(pbNew + nOffset));
        pt[0] = IMAGE_ORDINAL_FLAG_XX + 1;
        pt[1] = 0;
        piid[n].TimeDateStamp = 0;
        piid[n].ForwarderChain = 0;
        piid[n].Name = obBase + obStr;

		// Update available buffer for copying string name of next DLL.
        obStr += PadToDword((DWORD)strlen(plpDlls[n]) + 1);
    }

	// Print the IIDs in the in-memory buffer.
    for (DWORD i = 0; i < nDlls + (inh.IMPORT_DIRECTORY.Size / sizeof(*piid)); i++) {
        DETOUR_TRACE(("%8d. Look=%08x Time=%08x Fore=%08x Name=%08x Addr=%08x\n",
                      i,
                      piid[i].OriginalFirstThunk,
                      piid[i].TimeDateStamp,
                      piid[i].ForwarderChain,
                      piid[i].Name,
                      piid[i].FirstThunk));
        if (piid[i].OriginalFirstThunk == 0 && piid[i].FirstThunk == 0) {
            break;
        }
    }

	// Write the IIDs in the in-memory buffer to the allocated space in the PE file.
    if (!WriteProcessMemory(hProcess, pbNewIid, pbNew, obStr, NULL)) {
        DETOUR_TRACE_ERROR(L"WriteProcessMemory(iid) failed: %d\n", GetLastError());
        goto finish;
    }

    DETOUR_TRACE(("obBaseBef = %08x..%08x\n",
                  inh.IMPORT_DIRECTORY.VirtualAddress,
                  inh.IMPORT_DIRECTORY.VirtualAddress + inh.IMPORT_DIRECTORY.Size));
    DETOUR_TRACE(("obBaseAft = %08x..%08x\n", obBase, obBase + obStr));

    // If the file doesn't have an IAT_DIRECTORY, we create it...
    if (inh.IAT_DIRECTORY.VirtualAddress == 0) {
        inh.IAT_DIRECTORY.VirtualAddress = obBase;
        inh.IAT_DIRECTORY.Size = cbNew;
    }

	// Update the import directory in the PE header.
    inh.IMPORT_DIRECTORY.VirtualAddress = obBase;
    inh.IMPORT_DIRECTORY.Size = cbNew;

	//////////////////////// Get the CLR header.
#if BUILDXL_DETOURS

	*pbClr = NULL;

	if (inh.CLR_DIRECTORY.VirtualAddress != 0 && inh.CLR_DIRECTORY.Size != 0) {
		DETOUR_TRACE(("CLR.VirtAddr=%x, CLR.Size=%x\n", inh.CLR_DIRECTORY.VirtualAddress, inh.CLR_DIRECTORY.Size));
		*pbClr = ((PBYTE)hModule) + inh.CLR_DIRECTORY.VirtualAddress;
	}

#else
	*pbClr += 0;
#endif // BUILDXL_DETOURS

    /////////////////////// Update the NT header for the new import directory.
    /////////////////////////////// Update the DOS header to fix the checksum.
    //
    if (!VirtualProtectEx(hProcess, pbModule, inh.OptionalHeader.SizeOfHeaders,
                          PAGE_EXECUTE_READWRITE, &dwProtect)) {
        DETOUR_TRACE_ERROR(L"VirtualProtectEx(inh) write failed: %d\n", GetLastError());
        goto finish;
    }

#if IGNORE_CHECKSUMS
    idh.e_res[0] = 0;
#else
    inh.OptionalHeader.CheckSum = 0;
#endif // IGNORE_CHECKSUMS

	// Overwrite DOS header with the updated one.
    if (!WriteProcessMemory(hProcess, pbModule, &idh, sizeof(idh), NULL)) {
        DETOUR_TRACE_ERROR(L"WriteProcessMemory(idh) failed: %d\n", GetLastError());
        goto finish;
    }
    DETOUR_TRACE(("WriteProcessMemory(idh:%p..%p)\n", pbModule, pbModule + sizeof(idh)));

	// Overwrite PE header with the updated one.
    if (!WriteProcessMemory(hProcess, pbModule + idh.e_lfanew, &inh, sizeof(inh), NULL)) {
        DETOUR_TRACE_ERROR(L"WriteProcessMemory(inh) failed: %d\n", GetLastError());
        goto finish;
    }
    DETOUR_TRACE(("WriteProcessMemory(inh:%p..%p)\n",
                  pbModule + idh.e_lfanew,
                  pbModule + idh.e_lfanew + sizeof(inh)));

#if IGNORE_CHECKSUMS
    WORD wDuring = ComputeChkSum(hProcess, pbModule, &inh);
    DETOUR_TRACE(("ChkSum: %04x + %08x => %08x\n", wDuring, dwFileSize, wDuring + dwFileSize));

    idh.e_res[0] = detour_sum_minus(idh.e_res[0], detour_sum_minus(wDuring, wBefore));

    if (!WriteProcessMemory(hProcess, pbModule, &idh, sizeof(idh), NULL)) {
        DETOUR_TRACE_ERROR(L"WriteProcessMemory(idh) failed: %d\n", GetLastError());
        goto finish;
    }
#endif // IGNORE_CHECKSUMS

    if (!VirtualProtectEx(hProcess, pbModule, inh.OptionalHeader.SizeOfHeaders,
                          dwProtect, &dwProtect)) {
        DETOUR_TRACE_ERROR(L"VirtualProtectEx(idh) restore failed: %d\n", GetLastError());
        goto finish;
    }

#if IGNORE_CHECKSUMS
    WORD wAfter = ComputeChkSum(hProcess, pbModule, &inh);
    DETOUR_TRACE(("ChkSum: %04x + %08x => %08x\n", wAfter, dwFileSize, wAfter + dwFileSize));
    DETOUR_TRACE(("Before: %08x, After: %08x\n", wBefore + dwFileSize, wAfter + dwFileSize));

    if (wBefore != wAfter) {
        DETOUR_TRACE(("Restore of checksum failed %04x != %04x.\n", wBefore, wAfter));
        goto finish;
    }
#endif // IGNORE_CHECKSUMS

    fSucceeded = TRUE;
    goto finish;
}

