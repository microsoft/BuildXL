//////////////////////////////////////////////////////////////////////////////
//
//  Create a process with a DLL (creatwth.cpp of detours.lib)
//
//  Microsoft Research Detours Package, Version 3.0 Build_310.
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
// BuildXL-specific changes (forked from MSR version):
//  - Support for detouring 32-bit from 64-bit (without UpdImports)
//  - ETW tracing (see tracing.cpp).

#include "target.h"
#include <windows.h>
#include <stddef.h>
#include <strsafe.h>

// #define DETOUR_DEBUG 1
// #define IGNORE_CHECKSUMS 1
#define DETOURS_INTERNAL

//////////////////////////////////////////////////////////////////////////////
//
#define BUILDXL_DETOURS 1

#if BUILDXL_DETOURS
#include <assert.h>
#endif

#include "detours.h"
#include "tracing.h"

#define IMPORT_DIRECTORY OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT]
#define BOUND_DIRECTORY OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_BOUND_IMPORT]
#define CLR_DIRECTORY OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_COM_DESCRIPTOR]
#define IAT_DIRECTORY OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IAT]

//////////////////////////////////////////////////////////////////////////////
//
#if IGNORE_CHECKSUMS
static WORD detour_sum_minus(WORD wSum, WORD wMinus)
{
    wSum = (WORD)(wSum - ((wSum < wMinus) ? 1 : 0));
    wSum = (WORD)(wSum - wMinus);
    return wSum;
}

static WORD detour_sum_done(DWORD PartialSum)
{
    // Fold final carry into a single word result and return the resultant value.
    return (WORD)(((PartialSum >> 16) + PartialSum) & 0xffff);
}

static WORD detour_sum_data(DWORD dwSum, PBYTE pbData, DWORD cbData)
{
    while (cbData > 0) {
        dwSum += *((PWORD&)pbData)++;
        dwSum = (dwSum >> 16) + (dwSum & 0xffff);
        cbData -= sizeof(WORD);
    }
    return detour_sum_done(dwSum);
}

static WORD detour_sum_final(WORD wSum, PIMAGE_NT_HEADERS pinh)
{
    DETOUR_TRACE((".... : %08x (value: %08x)\n", wSum, pinh->OptionalHeader.CheckSum));

    // Subtract the two checksum words in the optional header from the computed.
    wSum = detour_sum_minus(wSum, ((PWORD)(&pinh->OptionalHeader.CheckSum))[0]);
    wSum = detour_sum_minus(wSum, ((PWORD)(&pinh->OptionalHeader.CheckSum))[1]);

    return wSum;
}

static WORD ChkSumRange(WORD wSum, HANDLE hProcess, PBYTE pbBeg, PBYTE pbEnd)
{
    BYTE rbPage[4096];

    while (pbBeg < pbEnd) {
        if (!ReadProcessMemory(hProcess, pbBeg, rbPage, sizeof(rbPage), NULL)) {
            DETOUR_TRACE_ERROR(L"ReadProcessMemory(%p, chk%p..%p) failed: %d\n",
                hProcess, pbBeg, pbEnd, GetLastError());
            break;
        }
        wSum = detour_sum_data(wSum, rbPage, sizeof(rbPage));
        pbBeg += sizeof(rbPage);
    }
    return wSum;
}

static WORD ComputeChkSum(HANDLE hProcess, PBYTE pbModule, PIMAGE_NT_HEADERS pinh)
{
    // See LdrVerifyMappedImageMatchesChecksum.

    MEMORY_BASIC_INFORMATION mbi;
    ZeroMemory(&mbi, sizeof(mbi));
    WORD wSum = 0;

    PBYTE pbLast = pbModule;
    for (;; pbLast = (PBYTE)mbi.BaseAddress + mbi.RegionSize) {
        ZeroMemory(&mbi, sizeof(mbi));
        if (VirtualQueryEx(hProcess, (PVOID)pbLast, &mbi, sizeof(mbi)) == 0) {
            if (GetLastError() == ERROR_INVALID_PARAMETER) {
                break;
            }
            DETOUR_TRACE_ERROR(L"VirtualQueryEx(%p, %p) failed: %d\n",
                hProcess, pbLast, GetLastError());
            break;
        }

        if (mbi.AllocationBase != pbModule) {
            break;
        }

        wSum = ChkSumRange(wSum,
                           hProcess,
                           (PBYTE)mbi.BaseAddress,
                           (PBYTE)mbi.BaseAddress + mbi.RegionSize);

        DETOUR_TRACE(("[%p..%p] : %04x\n",
                      (PBYTE)mbi.BaseAddress,
                      (PBYTE)mbi.BaseAddress + mbi.RegionSize,
                      wSum));
    }

    return detour_sum_final(wSum, pinh);
}
#endif // IGNORE_CHECKSUMS

//////////////////////////////////////////////////////////////////////////////
//
// Enumerate through modules in the target process.
//
static HMODULE WINAPI EnumerateModulesInProcess(HANDLE hProcess,
                                                HMODULE hModuleLast,
                                                PIMAGE_NT_HEADERS32 pNtHeader)
{
    PBYTE pbLast;

    if (hModuleLast == NULL) {
        pbLast = (PBYTE)0x10000;
    }
    else {
        pbLast = (PBYTE)hModuleLast + 0x10000;
    }

    MEMORY_BASIC_INFORMATION mbi;
    ZeroMemory(&mbi, sizeof(mbi));

    // Find the next memory region that contains a mapped PE image.
    //

    for (;; pbLast = (PBYTE)mbi.BaseAddress + mbi.RegionSize) {
        if (VirtualQueryEx(hProcess, (PVOID)pbLast, &mbi, sizeof(mbi)) == 0) {
            DETOUR_TRACE_VERBOSE(L"VirtualQueryEx(%p, %p) failed: %d\n",
                hProcess, (PVOID)pbLast, GetLastError());
            break;
        }
        if ((mbi.RegionSize & 0xfff) == 0xfff) {
            DETOUR_TRACE_ERROR(L"(mbi.RegionSize & 0xfff) == 0xfff\n");
            SetLastError(ERROR_INTERNAL_ERROR);
            break;
        }
        if (((PBYTE)mbi.BaseAddress + mbi.RegionSize) < pbLast) {
            DETOUR_TRACE_ERROR(L"((PBYTE)mbi.BaseAddress + mbi.RegionSize) < pbLast\n");
            SetLastError(ERROR_INTERNAL_ERROR);
            break;
        }

        // Skip uncommitted regions and guard pages.
        //
        if ((mbi.State != MEM_COMMIT) ||
            ((mbi.Protect & 0xff) == PAGE_NOACCESS) ||
            (mbi.Protect & PAGE_GUARD)) {
            continue;
        }

        __try {
            IMAGE_DOS_HEADER idh;
            if (!ReadProcessMemory(hProcess, pbLast, &idh, sizeof(idh), NULL)) {
                DETOUR_TRACE_ERROR(L"ReadProcessMemory(%p, idh%p..%p) failed: %d\n",
                    hProcess, pbLast, pbLast + sizeof(idh), GetLastError());
                continue;
            }

            if (idh.e_magic != IMAGE_DOS_SIGNATURE ||
                (DWORD)idh.e_lfanew > mbi.RegionSize ||
                (DWORD)idh.e_lfanew < sizeof(idh)) {
                continue;
            }

            if (!ReadProcessMemory(hProcess, pbLast + idh.e_lfanew,
                                   pNtHeader, sizeof(*pNtHeader), NULL)) {
                DETOUR_TRACE_ERROR(L"ReadProcessMemory(%p, inh%p..%p:%p) failed: %d\n",
                    hProcess,
                    pbLast + idh.e_lfanew,
                    pbLast + idh.e_lfanew + sizeof(*pNtHeader),
                    pbLast,
                    GetLastError());
                continue;
            }

            if (pNtHeader->Signature != IMAGE_NT_SIGNATURE) {
                continue;
            }

            return (HMODULE)pbLast;
        }
        __except(EXCEPTION_EXECUTE_HANDLER) {
            continue;
        }
    }

    // We can only get here via a 'break;' statement in the loop above.
    // In all those cases, a DETOUR_TRACE_ERROR was emitted, and a LastError established.
    return NULL;
}

//////////////////////////////////////////////////////////////////////////////
//
// Find a region of memory in which we can create a replacement import table.
//
static PBYTE FindAndAllocateNearBase(HANDLE hProcess, PBYTE pbBase, DWORD cbAlloc)
{
    MEMORY_BASIC_INFORMATION mbi;
    ZeroMemory(&mbi, sizeof(mbi));

    PBYTE pbLast = pbBase;
    for (;; pbLast = (PBYTE)mbi.BaseAddress + mbi.RegionSize) {

        ZeroMemory(&mbi, sizeof(mbi));
        if (VirtualQueryEx(hProcess, (PVOID)pbLast, &mbi, sizeof(mbi)) == 0) {
            if (GetLastError() == ERROR_INVALID_PARAMETER) {
                break;
            }
            DETOUR_TRACE_ERROR(L"VirtualQueryEx(%p, %p) failed: %d\n",
                hProcess, pbLast, GetLastError());
            break;
        }
        if ((mbi.RegionSize & 0xfff) == 0xfff) {
            DETOUR_TRACE_ERROR(L"(mbi.RegionSize & 0xfff) == 0xfff\n");
            SetLastError(ERROR_INTERNAL_ERROR);
            break;
        }

        // Skip anything other than a pure free region.
        //
        if (mbi.State != MEM_FREE) {
            continue;
        }

        PBYTE pbAddress = (PBYTE)(((DWORD_PTR)mbi.BaseAddress + 0xffff) & ~(DWORD_PTR)0xffff);

        DETOUR_TRACE(("Free region %p..%p\n",
                      mbi.BaseAddress,
                      (PBYTE)mbi.BaseAddress + mbi.RegionSize));

        for (; pbAddress < (PBYTE)mbi.BaseAddress + mbi.RegionSize; pbAddress += 0x10000) {
            PBYTE pbAlloc = (PBYTE)VirtualAllocEx(hProcess, pbAddress, cbAlloc,
                                                  MEM_RESERVE, PAGE_READWRITE);
            if (pbAlloc == NULL) {
                DETOUR_TRACE_ERROR(L"VirtualAllocEx(%p, %p) failed: %d\n", 
                    hProcess, pbAddress, GetLastError());
                continue;
            }
            pbAlloc = (PBYTE)VirtualAllocEx(hProcess, pbAddress, cbAlloc,
                                            MEM_COMMIT, PAGE_READWRITE);
            if (pbAlloc == NULL) {
                DETOUR_TRACE_ERROR(L"VirtualAllocEx(%p, %p) failed: %d\n", 
                    hProcess, pbAddress, GetLastError());
                continue;
            }
            DETOUR_TRACE(("[%p..%p] Allocated for import table.\n",
                          pbAlloc, pbAlloc + cbAlloc));
            return pbAlloc;
        }

    }

    // We can only get here via a 'break;' statement in the loop above.
    // In all those cases, a DETOUR_TRACE_ERROR was emitted, and a LastError established.
    return NULL;
}

static inline DWORD PadToDword(DWORD dw)
{
    return (dw + 3) & ~3u;
}

static inline DWORD PadToDwordPtr(DWORD dw)
{
    return (dw + 7) & ~7u;
}

//////////////////////////////////////////////////////////////////////////////
//
// For 32-bit Detours, only the function UpdateImports32 exists because we
// cannot detour 64-bit target process from a 32-bit process. The main
// reason for that is 32-bit Detours is unable to find the executable module
// of the target process because it has to retrieve information about a range
// of pages within the 64-bit virtual address space of the target process. 
//
// For 64-bit Detours, the functions UpdateImports32 and UpdateImports64 
// co-exist. The former is used for detouring 32-bit target process that needs
// to run on 32-bit platform, and the latter is for the rest. As one can see
// below, UpdateImportsXX uses XX-bit specific data structures to update
// the import table of PE. However, it does not justify why we can detour
// 32-bit process from 64-bit process by calling directly UpdateImports32. 
// One can argue that the below structs (e.g, IMAGE_NT_HEADERS32 and
// IMAGE_NT_HEADERS64) consist of fields whose types may have different
// sizes in 32-bit and in 64-bit platforms.
//
// Upon more detailed inspection, the DOS and PE headers (except the ULONGLONG
// fields in the optional header of 64-bit PE header) consist of fields
// of types LONG, BYTE, WORD, and DWORD. Fortunately, according to 
// the MSDN
//
//   http://msdn.microsoft.com/en-us/library/windows/desktop/aa383751(v=vs.85).aspx
//
// those types have the same size in both 32-bit and 64-bit platforms. 
// Thus, the size of DOS header and 32-bit PE header (IMAGE_NT_HEADERS32)
// in 64-bit platform are the same as the corresponding headers in 32-bit
// platform.

#if DETOURS_32BIT
#define DWORD_XX                        DWORD32
#define IMAGE_NT_HEADERS_XX             IMAGE_NT_HEADERS32
#define IMAGE_NT_OPTIONAL_HDR_MAGIC_XX  IMAGE_NT_OPTIONAL_HDR32_MAGIC
#define IMAGE_ORDINAL_FLAG_XX           IMAGE_ORDINAL_FLAG32
#define UPDATE_IMPORTS_XX               UpdateImports32
#include "uimports.cpp"
#undef DETOUR_EXE_RESTORE_FIELD_XX
#undef DWORD_XX
#undef IMAGE_NT_HEADERS_XX
#undef IMAGE_NT_OPTIONAL_HDR_MAGIC_XX
#undef IMAGE_ORDINAL_FLAG_XX
#undef UPDATE_IMPORTS_XX
#endif // DETOURS_32BIT

#if DETOURS_64BIT

#if BUILDXL_DETOURS
#define DWORD_XX                        DWORD32
#define IMAGE_NT_HEADERS_XX             IMAGE_NT_HEADERS32
#define IMAGE_NT_OPTIONAL_HDR_MAGIC_XX  IMAGE_NT_OPTIONAL_HDR32_MAGIC
#define IMAGE_ORDINAL_FLAG_XX           IMAGE_ORDINAL_FLAG32
#define UPDATE_IMPORTS_XX               UpdateImports32
#include "uimports.cpp"
#undef DETOUR_EXE_RESTORE_FIELD_XX
#undef DWORD_XX
#undef IMAGE_NT_HEADERS_XX
#undef IMAGE_NT_OPTIONAL_HDR_MAGIC_XX
#undef IMAGE_ORDINAL_FLAG_XX
#undef UPDATE_IMPORTS_XX
#endif // BUILDXL_DETOURS

#define DWORD_XX                        DWORD64
#define IMAGE_NT_HEADERS_XX             IMAGE_NT_HEADERS64
#define IMAGE_NT_OPTIONAL_HDR_MAGIC_XX  IMAGE_NT_OPTIONAL_HDR64_MAGIC
#define IMAGE_ORDINAL_FLAG_XX           IMAGE_ORDINAL_FLAG64
#define UPDATE_IMPORTS_XX               UpdateImports64
#include "uimports.cpp"
#undef DETOUR_EXE_RESTORE_FIELD_XX
#undef DWORD_XX
#undef IMAGE_NT_HEADERS_XX
#undef IMAGE_NT_OPTIONAL_HDR_MAGIC_XX
#undef IMAGE_ORDINAL_FLAG_XX
#undef UPDATE_IMPORTS_XX
#endif // DETOURS_64BIT

//////////////////////////////////////////////////////////////////////////////
//
#if DETOURS_64BIT

C_ASSERT(sizeof(IMAGE_NT_HEADERS64) == sizeof(IMAGE_NT_HEADERS32) + 16);

//////////////////////////////////////////////////////////////////////////////
//
// Replace the 32-bit PE header (IMAGE_NT_HEADER32) and section table with 
// the 64-bit ones (IMAGE_NT_HEADER64). This function is only called by 64-bit
// Detours when the target process has 32-bit executable but is not required to
// be run on 32-bit platform. (See DetourUpdateProcessWithDll for details.)
//
// One mystery about this function is why "upgrading" the PE header works.
// The 64-bit PE header is larger than the 32-bit one. The sizes of these headers
// are identical until the optional header part both on 32-bit and on 64-bit
// platforms. (Recall that, except the ULONGLONG fields in the optional header
// of the 64-bit PE header, other fields in the 32-bit and 64-bit PE headers
// are of types BYTE, WORD, and DWORD. Fortunately, both on 32-bit and 64-bit
// platforms each type has the same size.) The size difference between these two 
// headers is 16 bytes. For details, see
//
//   http://blogs.msdn.com/b/kstanton/archive/2004/03/31/105060.aspx
//
// But now there is a possibility that replacing the header with the 64-bit version 
// will overwrite other sections (most likely the .text section) in the PE file.
//
// Note that the characteristic of the target process indicates that it is
// .NET process without native code (no mixed-mode MSIL). For both 32-bit and
// 64-bit executables of such a process, the first 512 bytes of the PE comprises
// the DOS header, the PE header, and the section table consisting only of 
// three section headers, for .text, .reloc, and .rsrc. For the 32-bit executable,
// there are 16 bytes gap between the section table and the .text section.
// This gap is the place where the extra 16 bytes from the 64-bit PE header
// is stored. For details, see
//
//   http://www.ntcore.com/files/dotnetformat.htm
//   https://www.simple-talk.com/blogs/2011/03/15/anatomy-of-a-net-assembly-pe-headers/
//
static BOOL UpdateFrom32To64(HANDLE hProcess, HANDLE hModule, WORD machine)
{
    IMAGE_DOS_HEADER idh;
    IMAGE_NT_HEADERS32 inh32;
    IMAGE_NT_HEADERS64 inh64;
    IMAGE_SECTION_HEADER sects[32];
    PBYTE pbModule = (PBYTE)hModule;

    ZeroMemory(&inh32, sizeof(inh32));
    ZeroMemory(&inh64, sizeof(inh64));
    ZeroMemory(sects, sizeof(sects));

    DETOUR_TRACE(("UpdateFrom32To64(%04x)\n", machine));
    //////////////////////////////////////////////////////// Read old headers.
    //
    if (!ReadProcessMemory(hProcess, pbModule, &idh, sizeof(idh), NULL)) {
        DETOUR_TRACE_ERROR(L"ReadProcessMemory(%p, idh%p..%p) failed: %d\n",
            hProcess, pbModule, pbModule + sizeof(idh), GetLastError());
        return FALSE;
    }
    DETOUR_TRACE(("ReadProcessMemory(idh@%p..%p)\n",
                  pbModule, pbModule + sizeof(idh)));

    PBYTE pnh = pbModule + idh.e_lfanew;
    if (!ReadProcessMemory(hProcess, pnh, &inh32, sizeof(inh32), NULL)) {
        DETOUR_TRACE_ERROR(L"ReadProcessMemory(%p, inh%p..%p) failed: %d\n",
            hProcess, pnh, pnh + sizeof(inh32), GetLastError());
        return FALSE;
    }
    DETOUR_TRACE(("ReadProcessMemory(inh@%p..%p)\n", pnh, pnh + sizeof(inh32)));

    if (inh32.FileHeader.NumberOfSections > (sizeof(sects)/sizeof(sects[0]))) {
        DETOUR_TRACE_ERROR(L"inh32.FileHeader.NumberOfSections > (sizeof(sects)/sizeof(sects[0]))\n");
        SetLastError(ERROR_INTERNAL_ERROR);
        return FALSE;
    }

    PBYTE psects = pnh +
        FIELD_OFFSET(IMAGE_NT_HEADERS, OptionalHeader) +
        inh32.FileHeader.SizeOfOptionalHeader;
    ULONG cb = inh32.FileHeader.NumberOfSections * sizeof(IMAGE_SECTION_HEADER);
    if (!ReadProcessMemory(hProcess, psects, &sects, cb, NULL)) {
        DETOUR_TRACE_ERROR(L"ReadProcessMemory(%p, ish%p..%p) failed: %d\n",
            hProcess, psects, psects + cb, GetLastError());
        return FALSE;
    }
    DETOUR_TRACE(("ReadProcessMemory(ish@%p..%p)\n", psects, psects + cb));

    ////////////////////////////////////////////////////////// Convert header.
    //
    inh64.Signature = inh32.Signature;
    inh64.FileHeader = inh32.FileHeader;
    inh64.FileHeader.Machine = machine;
    inh64.FileHeader.SizeOfOptionalHeader = sizeof(IMAGE_OPTIONAL_HEADER64);

    inh64.OptionalHeader.Magic = IMAGE_NT_OPTIONAL_HDR64_MAGIC;
    inh64.OptionalHeader.MajorLinkerVersion = inh32.OptionalHeader.MajorLinkerVersion;
    inh64.OptionalHeader.MinorLinkerVersion = inh32.OptionalHeader.MinorLinkerVersion;
    inh64.OptionalHeader.SizeOfCode = inh32.OptionalHeader.SizeOfCode;
    inh64.OptionalHeader.SizeOfInitializedData = inh32.OptionalHeader.SizeOfInitializedData;
    inh64.OptionalHeader.SizeOfUninitializedData = inh32.OptionalHeader.SizeOfUninitializedData;
    inh64.OptionalHeader.AddressOfEntryPoint = inh32.OptionalHeader.AddressOfEntryPoint;
    inh64.OptionalHeader.BaseOfCode = inh32.OptionalHeader.BaseOfCode;
    inh64.OptionalHeader.ImageBase = inh32.OptionalHeader.ImageBase;
    inh64.OptionalHeader.SectionAlignment = inh32.OptionalHeader.SectionAlignment;
    inh64.OptionalHeader.FileAlignment = inh32.OptionalHeader.FileAlignment;
    inh64.OptionalHeader.MajorOperatingSystemVersion
        = inh32.OptionalHeader.MajorOperatingSystemVersion;
    inh64.OptionalHeader.MinorOperatingSystemVersion
        = inh32.OptionalHeader.MinorOperatingSystemVersion;
    inh64.OptionalHeader.MajorImageVersion = inh32.OptionalHeader.MajorImageVersion;
    inh64.OptionalHeader.MinorImageVersion = inh32.OptionalHeader.MinorImageVersion;
    inh64.OptionalHeader.MajorSubsystemVersion = inh32.OptionalHeader.MajorSubsystemVersion;
    inh64.OptionalHeader.MinorSubsystemVersion = inh32.OptionalHeader.MinorSubsystemVersion;
    inh64.OptionalHeader.Win32VersionValue = inh32.OptionalHeader.Win32VersionValue;
    inh64.OptionalHeader.SizeOfImage = inh32.OptionalHeader.SizeOfImage;
    inh64.OptionalHeader.SizeOfHeaders = inh32.OptionalHeader.SizeOfHeaders;
    inh64.OptionalHeader.CheckSum = inh32.OptionalHeader.CheckSum;
    inh64.OptionalHeader.Subsystem = inh32.OptionalHeader.Subsystem;
    inh64.OptionalHeader.DllCharacteristics = inh32.OptionalHeader.DllCharacteristics;
    inh64.OptionalHeader.SizeOfStackReserve = inh32.OptionalHeader.SizeOfStackReserve;
    inh64.OptionalHeader.SizeOfStackCommit = inh32.OptionalHeader.SizeOfStackCommit;
    inh64.OptionalHeader.SizeOfHeapReserve = inh32.OptionalHeader.SizeOfHeapReserve;
    inh64.OptionalHeader.SizeOfHeapCommit = inh32.OptionalHeader.SizeOfHeapCommit;
    inh64.OptionalHeader.LoaderFlags = inh32.OptionalHeader.LoaderFlags;
    inh64.OptionalHeader.NumberOfRvaAndSizes = inh32.OptionalHeader.NumberOfRvaAndSizes;
    for (DWORD n = 0; n < IMAGE_NUMBEROF_DIRECTORY_ENTRIES; n++) {
        inh64.OptionalHeader.DataDirectory[n] = inh32.OptionalHeader.DataDirectory[n];
    }

    inh64.IMPORT_DIRECTORY.VirtualAddress = 0;
    inh64.IMPORT_DIRECTORY.Size = 0;

    /////////////////////////////////////////////////////// Write new headers.
    //
    DWORD dwProtect = 0;
    if (!VirtualProtectEx(hProcess, pbModule, inh64.OptionalHeader.SizeOfHeaders,
                          PAGE_EXECUTE_READWRITE, &dwProtect)) {
        DETOUR_TRACE_ERROR(L"VirtualProtectEx(%p, %p) failed: %d\n", 
            hProcess, pbModule, GetLastError());
        return FALSE;
    }

    if (!WriteProcessMemory(hProcess, pnh, &inh64, sizeof(inh64), NULL)) {
        DETOUR_TRACE_ERROR(L"WriteProcessMemory(%p, inh%p..%p) failed: %d\n",
            hProcess, pnh, pnh + sizeof(inh64), GetLastError());
        return FALSE;
    }
    DETOUR_TRACE(("WriteProcessMemory(inh@%p..%p)\n", pnh, pnh + sizeof(inh64)));

    psects = pnh +
        FIELD_OFFSET(IMAGE_NT_HEADERS, OptionalHeader) +
        inh64.FileHeader.SizeOfOptionalHeader;
    cb = inh64.FileHeader.NumberOfSections * sizeof(IMAGE_SECTION_HEADER);
    if (!WriteProcessMemory(hProcess, psects, &sects, cb, NULL)) {
        DETOUR_TRACE_ERROR(L"WriteProcessMemory(%p, ish%p..%p) failed: %d\n",
            hProcess, psects, psects + cb, GetLastError());
        return FALSE;
    }
    DETOUR_TRACE(("WriteProcessMemory(ish@%p..%p)\n", psects, psects + cb));

    DWORD dwOld = 0;
    if (!VirtualProtectEx(hProcess, pbModule, inh64.OptionalHeader.SizeOfHeaders,
                          dwProtect, &dwOld)) {
        DETOUR_TRACE_ERROR(L"VirtualProtectEx(%p, %p) failed: %d\n", 
            hProcess, pbModule, GetLastError());
        return FALSE;
    }

    return TRUE;
}
#endif // DETOURS_64BIT

//////////////////////////////////////////////////////////////////////////////
//
BOOL WINAPI DetourUpdateProcessWithDll(HANDLE hProcess, __in_ecount(nDlls) LPCSTR *plpDlls, DWORD nDlls)
{
    // Find memory regions that contain mapped PE images to determine if the target process is 32-bit or 64-bit.
    //
    WORD mach32Bit = 0;
    WORD mach64Bit = 0;
    WORD exe32Bit = 0;
    HMODULE hModule = NULL;
    HMODULE hLast = NULL;

    // - exe32Bit: 
    //   - The value of this flag is non-zero (0x014c, for x86) if the target process has an executable that can run 
    //     on 32-bit platform. For managed applications, this value corresponds to PE32 of PE field output by CorFlags.
    //     The target process itself can be 64-bit process, e.g., a managed executable that is obtained by specifying
    //     the platform to be MSIL (or AnyCPU), without 32-bit preferred flag. 
    // - mach32Bit:
    //   - The value of this flag is non-zero (0x014c) if the target process must run on 32-bit platform because
    //     it loads 32-bit DLL(s). On 64-bit platform, this process will run under WOW64. The corresponding
    //     applications are typically obtained by specifically targeting x86 platform during compilation. 
    //     Furthermore, for managed applications, they may be obtained by setting its platform to MSIL (or AnyCPU),
    //     but with 32-bit preferred flag.
    // - mach64Bit:
    //   - The value of this flag is non-zero (0x0200, for IA64, or 0x8664, for x64) if the target process must run
    //     on 64-bit platform. We observed that, for process that can run on 32-bit platform, this flag is 
    //     also non-zero if the launching process is 64-bit process. (This observation came from our cross-bitness
    //     tests.)

    // The for-loop below enumerates modules in the target process. It finds the next memory region
    // that contains a mapped PE image. To this end, the function EnumerateModulesInProcess calls VirtualQueryEx
    // function to retrieve information about a range of pages within the virtual address space of the target
    // process. Thus, if the target process is 64-bit, and the launching process is 32-bit, then enumerating
    // the modules will fail to find PE image corresponding to an executable. This is the reason
    // why Detours launches a 64-bit UpdImports process to detour 64-bit process from 32-bit process.
    for (;;) {
        IMAGE_NT_HEADERS32 inh;

        if ((hLast = EnumerateModulesInProcess(hProcess, hLast, &inh)) == NULL) {
            break;
        }
            
        DETOUR_TRACE(("%p  machine=%04x magic=%04x\n",
                      hLast, inh.FileHeader.Machine, inh.OptionalHeader.Magic));

        if ((inh.FileHeader.Characteristics & IMAGE_FILE_DLL) == 0) {
            hModule = hLast;
            if (inh.OptionalHeader.Magic == IMAGE_NT_OPTIONAL_HDR32_MAGIC) {
                exe32Bit = inh.FileHeader.Machine;
            }
            DETOUR_TRACE(("%p  Found EXE\n", hLast));
        }
        else {
            if (inh.OptionalHeader.Magic == IMAGE_NT_OPTIONAL_HDR32_MAGIC) {
                mach32Bit = inh.FileHeader.Machine;
            }
            else if (inh.OptionalHeader.Magic == IMAGE_NT_OPTIONAL_HDR64_MAGIC) {
                mach64Bit = inh.FileHeader.Machine;
            }
        }
    }
    DETOUR_TRACE(("    exe32Bit=%04x mach32Bit=%04x mach64Bit=%04x\n", exe32Bit, mach32Bit, mach64Bit));
    
    if (hModule == NULL) {
        DETOUR_TRACE_ERROR(L"hModule == NULL\n");
        SetLastError(ERROR_INVALID_OPERATION);
        return FALSE;
    }

    // Save the various headers for DetourRestoreAfterWith.
    //
    DETOUR_EXE_RESTORE der;
    ZeroMemory(&der, sizeof(der));
    der.cb = sizeof(der);

    der.pidh = (PBYTE)hModule;
    der.cbidh = sizeof(der.idh);
    if (!ReadProcessMemory(hProcess, der.pidh, &der.idh, sizeof(der.idh), NULL)) {
        DETOUR_TRACE_ERROR(L"ReadProcessMemory(%p, idh%p..%p) failed: %d\n",
            hProcess, der.pidh, der.pidh + der.cbidh, GetLastError());
        return FALSE;
    }
    DETOUR_TRACE(("IDH: %p..%p\n", der.pidh, der.pidh + der.cbidh));

    // We read the NT header in two passes to get the full size. We first read the Signature and
    // FileHeader. The FileHeader contains information about the size of optional header and the
    // number of sections. Using that information, the next part of the NT header, i.e., the
    // optional header, can be correctly read.

    // (1) Read just the Signature and FileHeader of the NT header.
    der.pinh = der.pidh + der.idh.e_lfanew;
    der.cbinh = FIELD_OFFSET(IMAGE_NT_HEADERS, OptionalHeader);
    if (!ReadProcessMemory(hProcess, der.pinh, &der.inh, der.cbinh, NULL)) {
        DETOUR_TRACE_ERROR(L"ReadProcessMemory(%p, inh%p..%p) failed: %d\n",
            hProcess, der.pinh, der.pinh + der.cbinh, GetLastError());
        return FALSE;
    }

    // (2) Read the OptionalHeader and Section headers of the NT header.
    der.cbinh = (FIELD_OFFSET(IMAGE_NT_HEADERS, OptionalHeader) +
                 der.inh.FileHeader.SizeOfOptionalHeader +
                 der.inh.FileHeader.NumberOfSections * sizeof(IMAGE_SECTION_HEADER));
#if DETOURS_64BIT
    if (exe32Bit && !mach32Bit) {
        // Include the Save the extra 16-bytes that will be overwritten with 64-bit header.
        der.cbinh += sizeof(IMAGE_NT_HEADERS64) - sizeof(IMAGE_NT_HEADERS32);
    }
#endif // DETOURS_64BIT

    if (der.cbinh > sizeof(der.raw)) {
        DETOUR_TRACE_ERROR(L"der.cbinh > sizeof(der.raw)\n");
        SetLastError(ERROR_INTERNAL_ERROR);
        return FALSE;
    }

    if (!ReadProcessMemory(hProcess, der.pinh, &der.inh, der.cbinh, NULL)) {
        DETOUR_TRACE_ERROR(L"ReadProcessMemory(%p, inh%p..%p) failed: %d\n",
            hProcess, der.pinh, der.pinh + der.cbinh, GetLastError());
        return FALSE;
    }
    DETOUR_TRACE(("INH: %p..%p\n", der.pinh, der.pinh + der.cbinh));

#if BUILDXL_DETOURS == 0

    // Third, we read the CLR header

    if (der.inh.OptionalHeader.Magic == IMAGE_NT_OPTIONAL_HDR32_MAGIC) {
        if (der.inh32.CLR_DIRECTORY.VirtualAddress != 0 &&
            der.inh32.CLR_DIRECTORY.Size != 0) {
        }
        DETOUR_TRACE(("CLR32.VirtAddr=%x, CLR.Size=%x\n",
            der.inh32.CLR_DIRECTORY.VirtualAddress,
            der.inh32.CLR_DIRECTORY.Size));

        der.pclr = ((PBYTE)hModule) + der.inh32.CLR_DIRECTORY.VirtualAddress;
    }
    else if (der.inh.OptionalHeader.Magic == IMAGE_NT_OPTIONAL_HDR64_MAGIC) {
        if (der.inh64.CLR_DIRECTORY.VirtualAddress != 0 &&
            der.inh64.CLR_DIRECTORY.Size != 0) {
        }

        DETOUR_TRACE(("CLR64.VirtAddr=%x, CLR.Size=%x\n",
            der.inh64.CLR_DIRECTORY.VirtualAddress,
            der.inh64.CLR_DIRECTORY.Size));

        der.pclr = ((PBYTE)hModule) + der.inh64.CLR_DIRECTORY.VirtualAddress;
    }

    if (der.pclr != 0) {
        der.cbclr = sizeof(der.clr);
        if (!ReadProcessMemory(hProcess, der.pclr, &der.clr, der.cbclr, NULL)) {
            DETOUR_TRACE_ERROR(L"ReadProcessMemory(%p, clr%p..%p) failed: %d\n",
                hProcess, der.pclr, der.pclr + der.cbclr, GetLastError());
            return FALSE;
        }
        DETOUR_TRACE(("CLR: %p..%p\n", der.pclr, der.pclr + der.cbclr));
    }

    // Fourth, adjust for a 32-bit WOW64 process.

    if (exe32Bit && mach64Bit) {
        if (!der.pclr                       // Native binary
            || (der.clr.Flags & 1) == 0     // Or mixed-mode MSIL
            || (der.clr.Flags & 2) != 0) {  // Or 32BIT Required MSIL

            mach64Bit = 0;
            if (mach32Bit == 0) {
                mach32Bit = exe32Bit;
            }
        }
    }

    // The third step in the original Detours is read the CLR header. In this step
    // we had two cases whether the PE specifies 32-bit executable or 64-bit executable. 
    // For the former, we query IMAGE_NT_HEADER32 in DETOUR_EXE_RESTORE to get the virtual
    // address of the CLR, and for the latter we query IMAGE_NT_HEADER64. 
    //
    // First, these cases can be refactored by placing them in the corresponding UPDATE_IMPORTS_XX
    // functions. This turns out to be what MidBuild does. Second, there is a case below where
    // we try to convert 32-bit managed binary to a 64-bit managed binary by replacing its 
    // IMAGE_NT_HEADER32 with IMAGE_NT_HEADER64. Thus, reading the CLR header at this point
    // seems to be too early. As a remark, although it can cause some confusion, actually reading 
    // CLR header at this point is safe because, from the struct layout, the sizes of IMAGE_NT_HEADER32 and
    // IMAGE_NT_HEADER64 are identical until the optional header field, and the information
    // about the CLR header is in the file header field, which is before the optional header field.
    // (See the description of UpdateFrom32To64 for the reason why "upgrading" the PE header from
    // IMAGE_NT_HEADER32 to IMAGE_NT_HEADER64 works, and why that upgrading does not cause the location
    // of CLR header to change.)

    // The fourth step in the original Detours is adjust the 32-bit WOW64 process. In this step
    // if the target process can be run on 32-bit platform (exe32Bit is non-zero) and it is launched
    // from 64-bit process (or the target process itself is a 64-bit process), then if the target process
    // is a native binary, or is a mixed-mode MSIL, or is 32-bit required, then the adjustment makes
    // the process to be able to be run on 32-bit platform by ensuring that mach32Bit to be non-zero. 
    // The code is the following:
    //
    //   if (exe32Bit && mach64Bit) {
    //       if (!der.pclr                       // Native binary
    //           || (der.clr.Flags & 1) == 0     // Or mixed-mode MSIL
    //           || (der.clr.Flags & 2) != 0) {  // Or 32BIT Required MSIL
    //
    //           mach64Bit = 0;
    //           if (mach32Bit == 0) {
    //               mach32Bit = exe32Bit;
    //           }
    //		}
    //   }
    //
    // By ensuring that mach32Bit to be non-zero, the original Detours will terminate with failure 
    // because it was unable to detour 32-bit process from 64-bit one directly without going through 
    // the 32-bit UpdImports process.
    //
    // Upon close inspection, the assignment of 0 to mach64Bit is useless, and the then-block following
    // the "mach32Bit == 0" condition is unreachable. Let's assume that exe32Bit and mach64Bit are
    // non-zero. If the target process is native binary, then it must be built for a specific platform,
    // x86 or x64. For the former, the value of mach32Bit will be non-zero, and the original Detours will
    // terminate with failure. For the latter, the value of exe32Bit is zero, contradicting our assumption.
    //
    // If the target process is a mixed-mode MSIL. The executable is obtained by using the /clr option.
    // Because the executable will contain native code, then, similar to the previous case, it must be built
    // for a specific platform. Next, if the executable is 32-bit required (must be run on 32-bit platform,
    // or under WOW64 on 64-bit platform), then mach32Bit is already non-zero. Thus, similar to the previous
    // two cases, the original Detours will terminate with failure.
    //
    // Note that in the above code we query the CLR flags. Thus, in the original Detours, the CLR header
    // has to be read first before executing the above code.
    //
    // In the new Detours, for this case, we will either call UPDATE_IMPORTS_32 or try converting
    // the managed binary to a 64-bit managed binary, depending on the value of mach32Bit. If
    // the value of mach32Bit is non-zero, then we call UPDATE_IMPORTS_32, otherwise we try
    // converting it to 64-bit managed library.

#endif // BUILDXL_DETOURS
    
    // Now decide if we can insert the detour.

#if DETOURS_32BIT
    if (!mach32Bit && mach64Bit) {
        // 64-bit native or 64-bit managed process.
        //
        // Can't detour a 64-bit process with 32-bit code.
        // Note: This happens for 32-bit PE binaries containing only
        // manage code that have been marked as 64-bit ready.
        //
        DETOUR_TRACE_ERROR(L"!mach32Bit && mach64Bit\n");
        SetLastError(ERROR_INVALID_HANDLE);
        return FALSE;
    }
    else if (mach32Bit) {
        // 32-bit native or 32-bit managed process on any platform.
        if (!UpdateImports32(hProcess, hModule, plpDlls, nDlls, &(der.pclr))) {
            DETOUR_TRACE_ERROR(L"UpdateImports32(%p, %p) failed: %d\n", 
                hProcess, hModule, GetLastError());
            return FALSE;
        }
    }
    else {
        // Who knows!?
        DETOUR_TRACE_ERROR(L"!mach32Bit && !mach64Bit\n");
        SetLastError(ERROR_INVALID_HANDLE);
        return FALSE;
    }
#endif // DETOURS_32BIT

#if DETOURS_64BIT
    if (mach32Bit) {

#if BUILDXL_DETOURS

        // 32-bit native or 32-bit managed process on any platform.
        if (!UpdateImports32(hProcess, hModule, plpDlls, nDlls, &(der.pclr))) {
            DETOUR_TRACE_ERROR(L"UpdateImports32(%p, %p) failed: %d\n", 
                hProcess, hModule, GetLastError());
            return FALSE;
        }

#else

        // Can't detour a 32-bit process with 64-bit code.
        DETOUR_TRACE_ERROR(L"Can't detour a 32-bit process with 64-bit code.\n");
        SetLastError(ERROR_INVALID_HANDLE);
        return FALSE;

#endif // BUILDXL_DETOURS

    }
    else if (exe32Bit && !mach32Bit) {
        // Try to convert the 32-bit managed binary to a 64-bit managed binary.
        // UpdateFrom32To64 function replaces the 32-bit PE headers and section table
        // with 64-bit ones. This update is necessary since UpdateImports64 uses 
        // 64-bit specific data structure to update the import table.
        if (!UpdateFrom32To64(hProcess, hModule, mach64Bit)) {
            DETOUR_TRACE_ERROR(L"UpdateFrom32To64(%p, %p) failed: %d\n", 
                hProcess, hModule, GetLastError());
            return FALSE;
        }

        // 64-bit process from 32-bit managed binary.
        if (!UpdateImports64(hProcess, hModule, plpDlls, nDlls, &(der.pclr))) {
            DETOUR_TRACE_ERROR(L"UpdateImports64(%p, %p) failed: %d\n", 
                hProcess, hModule, GetLastError());
            return FALSE;
        }
    }
    else if (mach64Bit) {
        // 64-bit native or 64-bit managed process on any platform.
        if (!UpdateImports64(hProcess, hModule, plpDlls, nDlls, &(der.pclr))) {
            DETOUR_TRACE_ERROR(L"UpdateImports64(%p, %p) failed: %d\n", 
                hProcess, hModule, GetLastError());
            return FALSE;
        }
    }
    else {
        // Who knows!?
        DETOUR_TRACE_ERROR(L"!mach32Bit && !exe32Bit && !mach64Bit\n");
        SetLastError(ERROR_INVALID_HANDLE);
        return FALSE;
    }
#endif // DETOURS_64BIT

    /////////////////////////////////////////////////// Update the CLR header.
    //
    if (der.pclr != NULL) {
        der.cbclr = sizeof(der.clr);
        if (!ReadProcessMemory(hProcess, der.pclr, &der.clr, der.cbclr, NULL)) {
            DETOUR_TRACE_ERROR(L"ReadProcessMemory(%p, clr%p..%p) failed: %d\n",
                hProcess, der.pclr, der.pclr + der.cbclr, GetLastError());
            return FALSE;
        }
        DETOUR_TRACE(("CLR: %p..%p\n", der.pclr, der.pclr + der.cbclr));

        DETOUR_CLR_HEADER clr;
        CopyMemory(&clr, &der.clr, sizeof(clr));
        clr.Flags &= 0xfffffffe;    // Clear the IL_ONLY flag (because we inject unmanaged code).

        DWORD dwProtect;
        if (!VirtualProtectEx(hProcess, der.pclr, sizeof(clr), PAGE_READWRITE, &dwProtect)) {
            DETOUR_TRACE_ERROR(L"VirtualProtectEx(%p, clr%p) write failed: %d\n", 
                hProcess, der.pclr, GetLastError());
            return FALSE;
        }

        if (!WriteProcessMemory(hProcess, der.pclr, &clr, sizeof(clr), NULL)) {
            DETOUR_TRACE_ERROR(L"WriteProcessMemory(%p, clr%p) failed: %d\n", 
                hProcess, der.pclr, GetLastError());
            return FALSE;
        }

        if (!VirtualProtectEx(hProcess, der.pclr, sizeof(clr), dwProtect, &dwProtect)) {
            DETOUR_TRACE_ERROR(L"VirtualProtectEx(%p, clr%p) restore failed: %d\n", 
                hProcess, der.pclr, GetLastError());
            return FALSE;
        }
        DETOUR_TRACE(("CLR: %p..%p\n", der.pclr, der.pclr + der.cbclr));

#if DETOURS_64BIT

#if BUILDXL_DETOURS

        if ((der.clr.Flags & 0x2) && !mach32Bit) {
            // Is the 32BIT Required Flag set and we are not targetting mach32Bit?
            DETOUR_TRACE_ERROR(L"(der.clr.Flags & 0x2) && !mach32Bit\n");
            SetLastError(ERROR_INVALID_HANDLE);
            return FALSE;
        }

#else

        if (der.clr.Flags & 0x2) { // Is the 32BIT Required Flag set?
            // X64 never gets here because the process appears as a WOW64 process.
            // However, on IA64, it doesn't appear to be a WOW process.
            DETOUR_TRACE(("CLR Requires 32-bit\n", der.pclr, der.pclr + der.cbclr));
            SetLastError(ERROR_INVALID_HANDLE);
            return FALSE;
        }

#endif // BUILDXL_DETOURS

        // In the original Detours we have the following code here:
        //
        //   if (der.clr.Flags & 0x2) { // Is the 32BIT Required Flag set?
        //       // X64 never gets here because the process appears as a WOW64 process.
        //       // However, on IA64, it doesn't appear to be a WOW process.
        //       DETOUR_TRACE(("CLR Requires 32-bit\n", der.pclr, der.pclr + der.cbclr));
        //       SetLastError(ERROR_INVALID_HANDLE);
        //       return FALSE;
        //   }
        //
        // Both x64 and IA64 will never enter the then-block. First, the process that runs Detours 
        // is a 64-bit process (obviously from #if DETOURS_64BIT), and thus mach64Bit is non-zero. Second 
        // the target process is managed as it identifies CLR, and it is 32-bit process
        // because the 32-bit required flag is set. Thus, exe32Bit is non-zero. Recall that in the original
        // Detours there is a piece of code that adjusts 32-bit WOW64 process (see above). That piece of code
        // ensures that mach32Bit to be non-zero. Thus, the original Detours should have failed before
        // entering the above code.
#endif // DETOURS_64BIT
    }

    //////////////////////////////// Save the undo data to the target process.
    //
    if (!DetourCopyPayloadToProcess(hProcess, DETOUR_EXE_RESTORE_GUID, &der, sizeof(der))) {
        DETOUR_TRACE_ERROR(L"DetourCopyPayloadToProcess(%p) failed: %d\n", 
            hProcess, GetLastError());
        return FALSE;
    }
    return TRUE;
}

//////////////////////////////////////////////////////////////////////////////
//
BOOL WINAPI DetourCreateProcessWithDllA(LPCSTR lpApplicationName,
                                        __in_z LPSTR lpCommandLine,
                                        LPSECURITY_ATTRIBUTES lpProcessAttributes,
                                        LPSECURITY_ATTRIBUTES lpThreadAttributes,
                                        BOOL bInheritHandles,
                                        DWORD dwCreationFlags,
                                        LPVOID lpEnvironment,
                                        LPCSTR lpCurrentDirectory,
                                        LPSTARTUPINFOA lpStartupInfo,
                                        LPPROCESS_INFORMATION lpProcessInformation,
                                        LPCSTR lpDllName,
                                        PDETOUR_CREATE_PROCESS_ROUTINEA pfCreateProcessA)
{
    DWORD dwMyCreationFlags = (dwCreationFlags | CREATE_SUSPENDED);
    PROCESS_INFORMATION pi;

    if (pfCreateProcessA == NULL) {
        pfCreateProcessA = CreateProcessA;
    }

    if (!pfCreateProcessA(lpApplicationName,
                          lpCommandLine,
                          lpProcessAttributes,
                          lpThreadAttributes,
                          bInheritHandles,
                          dwMyCreationFlags,
                          lpEnvironment,
                          lpCurrentDirectory,
                          lpStartupInfo,
                          &pi)) {
        DETOUR_TRACE_ERROR(L"pfCreateProcessA(%S, %S) failed: %d\n",
            lpApplicationName, lpCommandLine, GetLastError());
        return FALSE;
    }

    LPCSTR rlpDlls[2];
    DWORD nDlls = 0;
    if (lpDllName != NULL) {
        rlpDlls[nDlls++] = lpDllName;
    }

    if (!DetourUpdateProcessWithDll(pi.hProcess, rlpDlls, nDlls)) {
        DETOUR_TRACE_ERROR(L"DetourUpdateProcessWithDll(%p) failed: %d\n", 
            pi.hProcess, GetLastError());

        DWORD error = GetLastError();
        if (!TerminateProcess(pi.hProcess, ~0u))
        {
            DETOUR_TRACE_ERROR(L"TerminateProcess(%p) failed: %d\n", 
                pi.hProcess, GetLastError());
        }
        SetLastError(error);

        return FALSE;
    }

    if (lpProcessInformation) {
        CopyMemory(lpProcessInformation, &pi, sizeof(pi));
    }

    if (!(dwCreationFlags & CREATE_SUSPENDED)) {
        ResumeThread(pi.hThread);
    }

    return TRUE;
}


BOOL WINAPI DetourCreateProcessWithDllW(LPCWSTR lpApplicationName,
                                        __in_z LPWSTR lpCommandLine,
                                        LPSECURITY_ATTRIBUTES lpProcessAttributes,
                                        LPSECURITY_ATTRIBUTES lpThreadAttributes,
                                        BOOL bInheritHandles,
                                        DWORD dwCreationFlags,
                                        LPVOID lpEnvironment,
                                        LPCWSTR lpCurrentDirectory,
                                        LPSTARTUPINFOW lpStartupInfo,
                                        LPPROCESS_INFORMATION lpProcessInformation,
                                        LPCSTR lpDllName,
                                        PDETOUR_CREATE_PROCESS_ROUTINEW pfCreateProcessW)
{
    DWORD dwMyCreationFlags = (dwCreationFlags | CREATE_SUSPENDED);
    PROCESS_INFORMATION pi;

    if (pfCreateProcessW == NULL) {
        pfCreateProcessW = CreateProcessW;
    }

    if (!pfCreateProcessW(lpApplicationName,
                          lpCommandLine,
                          lpProcessAttributes,
                          lpThreadAttributes,
                          bInheritHandles,
                          dwMyCreationFlags,
                          lpEnvironment,
                          lpCurrentDirectory,
                          lpStartupInfo,
                          &pi)) {
        DETOUR_TRACE_ERROR(L"pfCreateProcessW(%s, %s) failed: %d\n",
            lpApplicationName, lpCommandLine, GetLastError());
        return FALSE;
    }

    LPCSTR rlpDlls[2];
    DWORD nDlls = 0;
    if (lpDllName != NULL) {
        rlpDlls[nDlls++] = lpDllName;
    }

    if (!DetourUpdateProcessWithDll(pi.hProcess, rlpDlls, nDlls)) {
        DETOUR_TRACE_ERROR(L"DetourUpdateProcessWithDll(%p) failed: %d\n",
            pi.hProcess, GetLastError());

        DWORD error = GetLastError();
        if (!TerminateProcess(pi.hProcess, ~0u))
        {
            DETOUR_TRACE_ERROR(L"TerminateProcess(%p) failed: %d\n",
                pi.hProcess, GetLastError());
        }
        SetLastError(error);

        return FALSE;
    }

    if (lpProcessInformation) {
        CopyMemory(lpProcessInformation, &pi, sizeof(pi));
    }

    if (!(dwCreationFlags & CREATE_SUSPENDED)) {
        ResumeThread(pi.hThread);
    }
    return TRUE;
}

#ifdef DETOURS_X86_X64

// Returns FALSE if detouring should not be attempted for some reason.
// Else returns TRUE, and how to detour if the child proc is a different arch.
static BOOL NeedNewDetourProcess(
                          HANDLE hProcess,
                          LPCSTR lpDllNameX86,
                          LPCSTR lpDllNameX64,
                          LPCSTR *ppNewDll,
                          BOOL *pfNeedNewProc)
{
    BOOL fSuccess = TRUE;

    *ppNewDll = NULL;
    *pfNeedNewProc = FALSE;

#ifdef DETOURS_X64
    BOOL fChildIsWow;
    if (!IsWow64Process(hProcess, &fChildIsWow)) {
        fSuccess = FALSE;
    }
    else if (fChildIsWow) {
        *ppNewDll = lpDllNameX86;

#if BUILDXL_DETOURS == 0
        *pfNeedNewProc = TRUE;
#endif // BUILDXL_DETOURS

    }
    else
    {
        *ppNewDll = lpDllNameX64;
    }
#else
    BOOL fThisIsWow;
    BOOL fChildIsWow;
    if (!IsWow64Process(GetCurrentProcess(), &fThisIsWow) ||
        !IsWow64Process(hProcess, &fChildIsWow)) {
        fSuccess = FALSE;
    }
    else if (fThisIsWow != fChildIsWow)
    {
        *ppNewDll = lpDllNameX64;
        *pfNeedNewProc = TRUE;
    }
    else
    {
#if DISABLE_16_BIT_EXE_DETOURING
        CHAR szExeName[MAX_PATH];
        if (GetProcessImageFileName(hProcess, szExeName,
                                    ARRAYSIZE(szExeFileName)) == 0)
        {
            fSuccess = FALSE;
        }
        else
        {
            DWORD BinaryType;
            if (GetBinaryType(szExeName, &BinaryType) == 0 ||
                    BinaryType != SCS_32BIT_BINARY)
            {
                fSuccess = FALSE;
            }
            else
            {
                *ppNewDll = lpDllNameX86;
            }
        }
#else
        *ppNewDll = lpDllNameX86;
#endif
    }
#endif

    return fSuccess;
}

static BOOL LaunchImportUpdateExe(
    DWORD dwProcessId,
    LPCSTR lpLaucherExeX86,
    LPCSTR lpLaucherExeX64,
    LPCSTR lpDllName,
    PDETOUR_CREATE_PROCESS_ROUTINEA pfCreateProcessA,
    PDETOUR_CREATE_PROCESS_ROUTINEW pfCreateProcessW
    )
{
#ifdef DETOURS_X64
    (void)dwProcessId;          // not needed on this arch; avoid compiler warning
    (void)lpLaucherExeX64;      // not needed on this arch; avoid compiler warning
    (void)lpLaucherExeX86;      // not needed on this arch; avoid compiler warning
    (void)lpDllName;            // not needed on this arch; avoid compiler warning
    (void)pfCreateProcessA;     // not needed on this arch; avoid compiler warning
    (void)pfCreateProcessW;     // not needed on this arch; avoid compiler warning

    DETOUR_TRACE_ERROR(L"UpdImportsX86 is no longer needed\n");
    SetLastError(ERROR_INTERNAL_ERROR);
    return FALSE;
#else
    BOOL fResult;
    DWORD dwResult;
    LPCSTR lpLauncherExe;
    PROCESS_INFORMATION pi;

    (void)lpLaucherExeX86;   // not needed on this arch; avoid compiler warning
    lpLauncherExe = lpLaucherExeX64;

    if (lpLauncherExe == NULL)
    {
        DETOUR_TRACE_ERROR(L"lpLauncherExe == NULL\n");
        SetLastError(ERROR_INTERNAL_ERROR);
        return FALSE;
    }

    size_t cchCmdLine = 100 + strlen(lpLauncherExe);

    if (lpDllName != NULL)
        cchCmdLine += strlen(lpDllName);

    if (pfCreateProcessA != NULL)
    {
        STARTUPINFOA si;
        LPSTR szCmdLine;
        szCmdLine = new CHAR[cchCmdLine];
        if (szCmdLine == NULL)
        {
            DETOUR_TRACE_ERROR(L"szCmdLine == NULL\n");
            SetLastError(ERROR_OUTOFMEMORY);
            return FALSE;
        }

        _snprintf_s(szCmdLine, cchCmdLine, _TRUNCATE, "\"%s\" %d \"%s\"",
              lpLauncherExe, dwProcessId,
              (lpDllName != NULL ? lpDllName : ""));

        memset(&si, 0, sizeof(si));
        si.cb = sizeof(si);

        fResult = pfCreateProcessA(NULL,
                          szCmdLine,
                          NULL,
                          NULL,
                          FALSE,
                          0,
                          NULL,
                          NULL,
                          &si,
                          &pi);

        DWORD error = GetLastError();
        delete[] szCmdLine;
        SetLastError(error);
    }
    else
    {
        LPWSTR szCmdLine;
        szCmdLine = new WCHAR[cchCmdLine];
        if (szCmdLine == NULL)
        {
            DETOUR_TRACE_ERROR(L"szCmdLine == NULL\n");
            SetLastError(ERROR_OUTOFMEMORY);
            return FALSE;
        }

        _snwprintf_s(szCmdLine, cchCmdLine, _TRUNCATE, L"\"%S\" %d \"%S\"",
              lpLauncherExe, dwProcessId,
              (lpDllName != NULL ? lpDllName : ""));

        STARTUPINFOW si;

        memset(&si, 0, sizeof(si));
        si.cb = sizeof(si);

        fResult = pfCreateProcessW(NULL,
                          szCmdLine,
                          NULL,
                          NULL,
                          FALSE,
                          0,
                          NULL,
                          NULL,
                          &si,
                          &pi);

        DWORD error = GetLastError();
        delete[] szCmdLine;
        SetLastError(error);
    }

    if (!fResult)
        return FALSE;

    DWORD wfso = WaitForSingleObject(pi.hProcess, INFINITE);

    if (wfso != WAIT_OBJECT_0)
    {
        DETOUR_TRACE_ERROR(L"WaitForSingleObject(%p) failed with %d\n",
            pi.hProcess, wfso);
        dwResult = 10; // arbitrary failing code
    }
    else if (!GetExitCodeProcess(pi.hProcess, &dwResult))
    {
        DETOUR_TRACE_ERROR(L"GetExitCodeProcess(%p) failed with %d\n",
            pi.hProcess, GetLastError());
        dwResult = 11; // arbitrary failing code
    }
    else if (dwResult != 0)
    {
        DETOUR_TRACE_ERROR(L"Import Update process %p failed with exit code %d\n",
            pi.hProcess, dwResult);
    }

    CloseHandle(pi.hProcess);
    CloseHandle(pi.hThread);

    if (dwResult != 0)
    {
        // DETOUR_TRACE_ERROR_FUNC has already been issued
        SetLastError(ERROR_INTERNAL_ERROR);
        return FALSE;
    }

    SetLastError(NO_ERROR);
    return TRUE;
#endif
}

void WINAPI DetourCreateProcessWithDllx86x64A(
                          LPCSTR lpApplicationName,
                          __in_z LPSTR lpCommandLine,
                          LPSECURITY_ATTRIBUTES lpProcessAttributes,
                          LPSECURITY_ATTRIBUTES lpThreadAttributes,
                          BOOL bInheritHandles,
                          DWORD dwCreationFlags,
                          LPVOID lpEnvironment,
                          LPCSTR lpCurrentDirectory,
                          LPSTARTUPINFOA lpStartupInfo,
                          LPPROCESS_INFORMATION lpProcessInformation,
                          LPCSTR lpDllNameX86,
                          LPCSTR lpDllNameX64,
                          LPCSTR lpDetourLauchExeX86,
                          LPCSTR lpDetourLauchExeX64,
                          PDETOUR_CREATE_PROCESS_ROUTINEA pfCreateProcessA,
                          PBOOL pfProcCreated,
                          PBOOL pfProcDetoured)
{
    DWORD dwMyCreationFlags = (dwCreationFlags | CREATE_SUSPENDED);
    PROCESS_INFORMATION pi;

    if (pfCreateProcessA == NULL) {
        pfCreateProcessA = CreateProcessA;
    }

    *pfProcCreated = FALSE;
    *pfProcDetoured = FALSE;

    if (!pfCreateProcessA(lpApplicationName,
                          lpCommandLine,
                          lpProcessAttributes,
                          lpThreadAttributes,
                          bInheritHandles,
                          dwMyCreationFlags,
                          lpEnvironment,
                          lpCurrentDirectory,
                          lpStartupInfo,
                          &pi)) {
        DETOUR_TRACE_ERROR(L"pfCreateProcessW(%S, %S, ...) failed: %d\n", 
            lpApplicationName, lpCommandLine, GetLastError());
        return;
    }

    *pfProcCreated = TRUE;

    BOOL fNeedNewProc;
    LPCSTR lpDllName = NULL;

    if (NeedNewDetourProcess(pi.hProcess,
              lpDllNameX86, lpDllNameX64,
              &lpDllName, &fNeedNewProc))
    {
        // Apply the detours directly if we're on the same architecture.
        if (!fNeedNewProc)
        {
            LPCSTR rlpDlls[2];
            DWORD nDlls = 0;

            if (lpDllName != NULL) {
                rlpDlls[nDlls++] = lpDllName;
            }

            if (!DetourUpdateProcessWithDll(pi.hProcess, rlpDlls, nDlls)) {
                DETOUR_TRACE_ERROR(L"DetourUpdateProcessWithDll(%d) failed: %d\n",
                    pi.hProcess, GetLastError());
            }
            else {
                *pfProcDetoured = TRUE;
            }
        }
        else   // switching architectures
        {
            if (!LaunchImportUpdateExe(pi.dwProcessId,
                lpDetourLauchExeX86, lpDetourLauchExeX64,
                lpDllName,
                pfCreateProcessA, NULL))
            {
                DETOUR_TRACE_ERROR(L"LaunchImportUpdateExe(%d, %S, %S, %S) failed: %d\n",
                    pi.dwProcessId,
                    lpDetourLauchExeX86, lpDetourLauchExeX64,
                    lpDllName,
                    GetLastError());
            }
            else {
                *pfProcDetoured = TRUE;
            }
        }
    }

    if (lpProcessInformation) {
        CopyMemory(lpProcessInformation, &pi, sizeof(pi));
    }

    if (!(dwCreationFlags & CREATE_SUSPENDED)) {
        ResumeThread(pi.hThread);
    }
    return;
}


void WINAPI DetourCreateProcessWithDllx86x64W(
                          LPCWSTR lpApplicationName,
                          __in_z LPWSTR lpCommandLine,
                          LPSECURITY_ATTRIBUTES lpProcessAttributes,
                          LPSECURITY_ATTRIBUTES lpThreadAttributes,
                          BOOL bInheritHandles,
                          DWORD dwCreationFlags,
                          LPVOID lpEnvironment,
                          LPCWSTR lpCurrentDirectory,
                          LPSTARTUPINFOW lpStartupInfo,
                          LPPROCESS_INFORMATION lpProcessInformation,
                          LPCSTR lpDllNameX86,
                          LPCSTR lpDllNameX64,
                          LPCSTR lpDetourLauchExeX86,
                          LPCSTR lpDetourLauchExeX64,
                          PDETOUR_CREATE_PROCESS_ROUTINEW pfCreateProcessW,
                          PBOOL pfProcCreated,
                          PBOOL pfProcDetoured)
{
    DWORD dwMyCreationFlags = (dwCreationFlags | CREATE_SUSPENDED);
    PROCESS_INFORMATION pi;

    if (pfCreateProcessW == NULL) {
        pfCreateProcessW = CreateProcessW;
    }

    *pfProcCreated = FALSE;
    *pfProcDetoured = FALSE;

    if (!pfCreateProcessW(lpApplicationName,
                          lpCommandLine,
                          lpProcessAttributes,
                          lpThreadAttributes,
                          bInheritHandles,
                          dwMyCreationFlags,
                          lpEnvironment,
                          lpCurrentDirectory,
                          lpStartupInfo,
                          &pi)) {
        DETOUR_TRACE_ERROR(L"pfCreateProcessW(%ls, %ls, ...) failed: %d\n", 
            lpApplicationName, lpCommandLine, GetLastError());
        return;
    }

    *pfProcCreated = TRUE;

    BOOL fNeedNewProc;
    LPCSTR lpDllName = NULL;
    if (NeedNewDetourProcess(pi.hProcess,
              lpDllNameX86, lpDllNameX64,
              &lpDllName, &fNeedNewProc))
    {
        // Apply the detours directly if we're on the same architecture.
        if (!fNeedNewProc)
        {
            LPCSTR rlpDlls[2];
            DWORD nDlls = 0;

            if (lpDllName != NULL) {
                rlpDlls[nDlls++] = lpDllName;
            }
            if (!DetourUpdateProcessWithDll(pi.hProcess, rlpDlls, nDlls)) {
                DETOUR_TRACE_ERROR(L"DetourUpdateProcessWithDll(%d) failed: %d\n",
                    pi.hProcess, GetLastError());
            }
            else {
                *pfProcDetoured = TRUE;
            }
        }
        else   // switching architectures
        {
            if (!LaunchImportUpdateExe(pi.dwProcessId,
                lpDetourLauchExeX86, lpDetourLauchExeX64,
                lpDllName,
                NULL, pfCreateProcessW))
            {
                DETOUR_TRACE_ERROR(L"LaunchImportUpdateExe(%d, %S, %S, %S) failed: %d\n",
                    pi.dwProcessId,
                    lpDetourLauchExeX86, lpDetourLauchExeX64,
                    lpDllName,
                    GetLastError());
            }
            else {
                *pfProcDetoured = TRUE;
            }
        }
    }

    if (lpProcessInformation) {
        CopyMemory(lpProcessInformation, &pi, sizeof(pi));
    }

    if (!(dwCreationFlags & CREATE_SUSPENDED)) {
        ResumeThread(pi.hThread);
    }
    return;
}

#endif // DETOURS_X86_X64

BOOL WINAPI DetourCopyPayloadToProcess(HANDLE hProcess,
                                       REFGUID rguid,
                                       PVOID pData,
                                       DWORD cbData)
{
    DWORD cbTotal = (sizeof(IMAGE_DOS_HEADER) +
                     sizeof(IMAGE_NT_HEADERS) +
                     sizeof(IMAGE_SECTION_HEADER) +
                     sizeof(DETOUR_SECTION_HEADER) +
                     sizeof(DETOUR_SECTION_RECORD) +
                     cbData);

    PBYTE pbBase = (PBYTE)VirtualAllocEx(hProcess, NULL, cbTotal,
                                         MEM_COMMIT, PAGE_READWRITE);
    if (pbBase == NULL) {
        DETOUR_TRACE_ERROR(L"VirtualAllocEx(%p, %d) failed: %d\n", 
            hProcess, cbTotal, GetLastError());
        return FALSE;
    }

    PBYTE pbTarget = pbBase;
    IMAGE_DOS_HEADER idh;
    IMAGE_NT_HEADERS inh;
    IMAGE_SECTION_HEADER ish;
    DETOUR_SECTION_HEADER dsh;
    DETOUR_SECTION_RECORD dsr;
    SIZE_T cbWrote = 0;

    ZeroMemory(&idh, sizeof(idh));
    idh.e_magic = IMAGE_DOS_SIGNATURE;
    idh.e_lfanew = sizeof(idh);
    if (!WriteProcessMemory(hProcess, pbTarget, &idh, sizeof(idh), &cbWrote) ||
        cbWrote != sizeof(idh)) {
        DETOUR_TRACE_ERROR(L"WriteProcessMemory(%p, idh%p) failed: %d\n", 
            hProcess, pbTarget, GetLastError());
        return FALSE;
    }
    pbTarget += sizeof(idh);

    ZeroMemory(&inh, sizeof(inh));
    inh.Signature = IMAGE_NT_SIGNATURE;
    inh.FileHeader.SizeOfOptionalHeader = sizeof(inh.OptionalHeader);
    inh.FileHeader.Characteristics = IMAGE_FILE_DLL;
    inh.FileHeader.NumberOfSections = 1;
    inh.OptionalHeader.Magic = IMAGE_NT_OPTIONAL_HDR_MAGIC;
    if (!WriteProcessMemory(hProcess, pbTarget, &inh, sizeof(inh), &cbWrote) ||
        cbWrote != sizeof(inh)) {
        DETOUR_TRACE_ERROR(L"WriteProcessMemory(%p, inh%p) failed: %d\n", 
            hProcess, pbTarget, GetLastError());
        return FALSE;
    }
    pbTarget += sizeof(inh);

    ZeroMemory(&ish, sizeof(ish));
    memcpy(ish.Name, ".detour", sizeof(ish.Name));
    ish.VirtualAddress = (DWORD)((pbTarget + sizeof(ish)) - pbBase);
    ish.SizeOfRawData = (sizeof(DETOUR_SECTION_HEADER) +
                         sizeof(DETOUR_SECTION_RECORD) +
                         cbData);
    if (!WriteProcessMemory(hProcess, pbTarget, &ish, sizeof(ish), &cbWrote) ||
        cbWrote != sizeof(ish)) {
        DETOUR_TRACE_ERROR(L"WriteProcessMemory(%p, ish%p) failed: %d\n", 
            hProcess, pbTarget, GetLastError());
        return FALSE;
    }
    pbTarget += sizeof(ish);

    ZeroMemory(&dsh, sizeof(dsh));
    dsh.cbHeaderSize = sizeof(dsh);
    dsh.nSignature = DETOUR_SECTION_HEADER_SIGNATURE;
    dsh.nDataOffset = sizeof(DETOUR_SECTION_HEADER);
    dsh.cbDataSize = (sizeof(DETOUR_SECTION_HEADER) +
                      sizeof(DETOUR_SECTION_RECORD) +
                      cbData);
    if (!WriteProcessMemory(hProcess, pbTarget, &dsh, sizeof(dsh), &cbWrote) ||
        cbWrote != sizeof(dsh)) {
        DETOUR_TRACE_ERROR(L"WriteProcessMemory(%p, dsh%p) failed: %d\n", 
            hProcess, pbTarget, GetLastError());
        return FALSE;
    }
    pbTarget += sizeof(dsh);

    ZeroMemory(&dsr, sizeof(dsr));
    dsr.cbBytes = cbData + sizeof(DETOUR_SECTION_RECORD);
    dsr.nReserved = 0;
    dsr.guid = rguid;
    if (!WriteProcessMemory(hProcess, pbTarget, &dsr, sizeof(dsr), &cbWrote) ||
        cbWrote != sizeof(dsr)) {
        DETOUR_TRACE_ERROR(L"WriteProcessMemory(%p, dsr%p) failed: %d\n", 
            hProcess, pbTarget, GetLastError());
        return FALSE;
    }
    pbTarget += sizeof(dsr);

    if (!WriteProcessMemory(hProcess, pbTarget, pData, cbData, &cbWrote) ||
        cbWrote != cbData) {
        DETOUR_TRACE_ERROR(L"WriteProcessMemory(%p, pData%p) failed: %d\n", 
            hProcess, pbTarget, GetLastError());
        return FALSE;
    }
    pbTarget += cbData;

    DETOUR_TRACE(("Copied %d byte payload into target process at %p\n",
                  cbTotal, pbTarget - cbTotal));
    return TRUE;
}

//
///////////////////////////////////////////////////////////////// End of File.
