//////////////////////////////////////////////////////////////////////////////
//
//  Module Enumeration Functions (modules.cpp of detours.lib)
//
//  Microsoft Research Detours Package, Version 3.0 Build_310.
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  Module enumeration functions.
//
// BuildXL-specific changes (forked from MSR version):
//  - ETW tracing (see tracing.cpp).

#include "target.h"
#include <windows.h>
#include <strsafe.h>

//#define DETOUR_DEBUG 1
#define DETOURS_INTERNAL
#include "detours.h"
#include "tracing.h"

#define CLR_DIRECTORY OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_COM_DESCRIPTOR]
#define IAT_DIRECTORY OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IAT]


//////////////////////////////////////////////////////////////////////////////
//
const GUID DETOUR_EXE_RESTORE_GUID = {
    0x2ed7a3ff, 0x3339, 0x4a8d,
    { 0x80, 0x5c, 0xd4, 0x98, 0x15, 0x3f, 0xc2, 0x8f }};

//////////////////////////////////////////////////////////////////////////////
//

#if 0
FORCEINLINE BOOL
DetourIsWindowsVersionOrGreater(WORD wMajorVersion, WORD wMinorVersion, WORD wServicePackMajor)
{
    OSVERSIONINFOEXW osvi = { sizeof(osvi), 0, 0, 0, 0,{ 0 }, 0, 0 };
    DWORDLONG        const dwlConditionMask = VerSetConditionMask(
        VerSetConditionMask(
            VerSetConditionMask(
                0, VER_MAJORVERSION, VER_GREATER_EQUAL),
            VER_MINORVERSION, VER_GREATER_EQUAL),
        VER_SERVICEPACKMAJOR, VER_GREATER_EQUAL);

    osvi.dwMajorVersion = wMajorVersion;
    osvi.dwMinorVersion = wMinorVersion;
    osvi.wServicePackMajor = wServicePackMajor;

    return VerifyVersionInfoW(&osvi, VER_MAJORVERSION | VER_MINORVERSION | VER_SERVICEPACKMAJOR, dwlConditionMask) != FALSE;
}
#endif

PDETOUR_SYM_INFO DetourLoadImageHlp(VOID)
{
    static DETOUR_SYM_INFO symInfo;
    static PDETOUR_SYM_INFO pSymInfo = NULL;
    static BOOL failed = false;

    if (failed) {
        DETOUR_TRACE_ERROR(L"DetourLoadImageHlp failed earlier\n");
        return NULL;
    }
    if (pSymInfo != NULL) {
        return pSymInfo;
    }

    ZeroMemory(&symInfo, sizeof(symInfo));
    // Create a real handle to the process.
#if 0
    DuplicateHandle(GetCurrentProcess(),
                    GetCurrentProcess(),
                    GetCurrentProcess(),
                    &symInfo.hProcess,
                    0,
                    FALSE,
                    DUPLICATE_SAME_ACCESS);
#else
    symInfo.hProcess = GetCurrentProcess();
#endif

    symInfo.hDbgHelp = LoadLibraryExW(L"dbghelp.dll", NULL, 0);
    if (symInfo.hDbgHelp == NULL) {
      abort:
        failed = true;
        if (symInfo.hDbgHelp != NULL) {
            FreeLibrary(symInfo.hDbgHelp);
        }
        symInfo.pfImagehlpApiVersionEx = NULL;
        symInfo.pfSymInitialize = NULL;
        symInfo.pfSymSetOptions = NULL;
        symInfo.pfSymGetOptions = NULL;
        symInfo.pfSymLoadModule64 = NULL;
        symInfo.pfSymGetModuleInfo64 = NULL;
        symInfo.pfSymFromName = NULL;
        return NULL;
    }

    symInfo.pfImagehlpApiVersionEx
        = (PF_ImagehlpApiVersionEx)GetProcAddress(symInfo.hDbgHelp,
                                                  "ImagehlpApiVersionEx");
    symInfo.pfSymInitialize
        = (PF_SymInitialize)GetProcAddress(symInfo.hDbgHelp, "SymInitialize");
    symInfo.pfSymSetOptions
        = (PF_SymSetOptions)GetProcAddress(symInfo.hDbgHelp, "SymSetOptions");
    symInfo.pfSymGetOptions
        = (PF_SymGetOptions)GetProcAddress(symInfo.hDbgHelp, "SymGetOptions");
    symInfo.pfSymLoadModule64
        = (PF_SymLoadModule64)GetProcAddress(symInfo.hDbgHelp, "SymLoadModule64");
    symInfo.pfSymGetModuleInfo64
        = (PF_SymGetModuleInfo64)GetProcAddress(symInfo.hDbgHelp, "SymGetModuleInfo64");
    symInfo.pfSymFromName
        = (PF_SymFromName)GetProcAddress(symInfo.hDbgHelp, "SymFromName");

    API_VERSION av;
    ZeroMemory(&av, sizeof(av));
    av.MajorVersion = API_VERSION_NUMBER;

    if (symInfo.pfImagehlpApiVersionEx == NULL ||
        symInfo.pfSymInitialize == NULL ||
        symInfo.pfSymLoadModule64 == NULL ||
        symInfo.pfSymGetModuleInfo64 == NULL ||
        symInfo.pfSymFromName == NULL) {
        DETOUR_TRACE_ERROR(L"something was NULL\n");
        goto abort;
    }

    symInfo.pfImagehlpApiVersionEx(&av);
    if (av.MajorVersion < API_VERSION_NUMBER) {
        DETOUR_TRACE_ERROR(L"av.MajorVersion < API_VERSION_NUMBER\n");
        goto abort;
    }

    if (!symInfo.pfSymInitialize(symInfo.hProcess, NULL, FALSE)) {
        // We won't retry the initialize if it fails.
        DETOUR_TRACE_ERROR(L"!symInfo.pfSymInitialize(%p)\n", symInfo.hProcess);
        goto abort;
    }

    if (symInfo.pfSymGetOptions != NULL && symInfo.pfSymSetOptions != NULL) {
        DWORD dw = symInfo.pfSymGetOptions();

        dw &= ~(SYMOPT_CASE_INSENSITIVE |
                SYMOPT_UNDNAME |
                SYMOPT_DEFERRED_LOADS |
                0);
        dw |= (
#if defined(SYMOPT_EXACT_SYMBOLS)
               SYMOPT_EXACT_SYMBOLS |
#endif
#if defined(SYMOPT_NO_UNQUALIFIED_LOADS)
               SYMOPT_NO_UNQUALIFIED_LOADS |
#endif
               SYMOPT_DEFERRED_LOADS |
#if defined(SYMOPT_FAIL_CRITICAL_ERRORS)
               SYMOPT_FAIL_CRITICAL_ERRORS |
#endif
#if defined(SYMOPT_INCLUDE_32BIT_MODULES)
               SYMOPT_INCLUDE_32BIT_MODULES |
#endif
               0);
        symInfo.pfSymSetOptions(dw);
    }

    pSymInfo = &symInfo;
    return pSymInfo;
}

PVOID WINAPI DetourFindFunction(__in_z PCSTR pszModule, __in_z PCSTR pszFunction)
{
    /////////////////////////////////////////////// First, try GetProcAddress.
    //
    HMODULE hModule = LoadLibraryExA(pszModule, NULL, 0);
    if (hModule == NULL) {
        DETOUR_TRACE_ERROR(L"LoadLibraryExA(%S) failed: %d\n",
            pszModule, GetLastError());
        return NULL;
    }

    PBYTE pbCode = (PBYTE)GetProcAddress(hModule, pszFunction);
    if (pbCode) {
        return pbCode;
    }

    ////////////////////////////////////////////////////// Then try ImageHelp.
    //
    DETOUR_TRACE(("DetourFindFunction(%S, %S)\n", pszModule, pszFunction));
    PDETOUR_SYM_INFO pSymInfo = DetourLoadImageHlp();
    if (pSymInfo == NULL) {
        DETOUR_TRACE_ERROR(L"DetourLoadImageHlp on (%S, %S) failed: %d\n",
            pszModule, pszFunction, GetLastError());
        return NULL;
    }

    if (pSymInfo->pfSymLoadModule64(pSymInfo->hProcess, NULL,
                                    (PCHAR)pszModule, NULL,
                                    (DWORD64)hModule, 0) == 0) {
        if (ERROR_SUCCESS != GetLastError()) {
            DETOUR_TRACE_ERROR(L"SymLoadModule64(%p, %S, %p) failed: %d\n",
                pSymInfo->hProcess, (PCHAR)pszModule, hModule, GetLastError());
            return NULL;
        }
    }

    HRESULT hrRet;
    CHAR szFullName[512];
    IMAGEHLP_MODULE64 modinfo;
    ZeroMemory(&modinfo, sizeof(modinfo));
    modinfo.SizeOfStruct = sizeof(modinfo);
    if (!pSymInfo->pfSymGetModuleInfo64(pSymInfo->hProcess, (DWORD64)hModule, &modinfo)) {
        DETOUR_TRACE_ERROR(L"SymGetModuleInfo64(%p, %p) failed: %d\n",
            pSymInfo->hProcess, hModule, GetLastError());
        return NULL;
    }

    hrRet = StringCchCopyA(szFullName, sizeof(szFullName)/sizeof(CHAR), modinfo.ModuleName);
    if (FAILED(hrRet)) {
        DETOUR_TRACE_ERROR(L"StringCchCopyA failed: %08x\n", hrRet);
        return NULL;
    }
    hrRet = StringCchCatA(szFullName, sizeof(szFullName)/sizeof(CHAR), "!");
    if (FAILED(hrRet)) {
        DETOUR_TRACE_ERROR(L"StringCchCatA failed: %08x\n", hrRet);
        return NULL;
    }
    hrRet = StringCchCatA(szFullName, sizeof(szFullName)/sizeof(CHAR), pszFunction);
    if (FAILED(hrRet)) {
        DETOUR_TRACE_ERROR(L"StringCchCatA failed: %08x\n", hrRet);
        return NULL;
    }

    struct CFullSymbol : SYMBOL_INFO {
        CHAR szRestOfName[512];
    } symbol;
    ZeroMemory(&symbol, sizeof(symbol));
    //symbol.ModBase = (ULONG64)hModule;
    symbol.SizeOfStruct = sizeof(SYMBOL_INFO);
#ifdef DBHLPAPI
    symbol.MaxNameLen = sizeof(symbol.szRestOfName)/sizeof(symbol.szRestOfName[0]);
#else
    symbol.MaxNameLength = sizeof(symbol.szRestOfName)/sizeof(symbol.szRestOfName[0]);
#endif

    if (!pSymInfo->pfSymFromName(pSymInfo->hProcess, szFullName, &symbol)) {
        DETOUR_TRACE_ERROR(L"SymFromName(%p, %S) failed: %d\n", 
            pSymInfo->hProcess, szFullName, GetLastError());
        return NULL;
    }

#ifdef DETOURS_IA64
    // On the IA64, we get a raw code pointer from the symbol engine
    // and have to convert it to a wrapped [code pointer, global pointer].
    //
    PPLABEL_DESCRIPTOR pldEntry = (PPLABEL_DESCRIPTOR)DetourGetEntryPoint(hModule);
    PPLABEL_DESCRIPTOR pldSymbol = new PLABEL_DESCRIPTOR;

    pldSymbol->EntryPoint = symbol.Address;
    pldSymbol->GlobalPointer = pldEntry->GlobalPointer;
    return (PBYTE)pldSymbol;
#else
    return (PBYTE)symbol.Address;
#endif
}

//////////////////////////////////////////////////// Module Image Functions.
//
HMODULE WINAPI DetourEnumerateModules(HMODULE hModuleLast)
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
        if (VirtualQuery((PVOID)pbLast, &mbi, sizeof(mbi)) <= 0) {
            DETOUR_TRACE_VERBOSE(L"VirtualQuery(%p) failed: %d\n",
                (PVOID)pbLast, GetLastError());
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
            PIMAGE_DOS_HEADER pDosHeader = (PIMAGE_DOS_HEADER)pbLast;
            if (pDosHeader->e_magic != IMAGE_DOS_SIGNATURE ||
                (DWORD)pDosHeader->e_lfanew > mbi.RegionSize ||
                (DWORD)pDosHeader->e_lfanew < sizeof(*pDosHeader)) {
                continue;
            }

            PIMAGE_NT_HEADERS pNtHeader = (PIMAGE_NT_HEADERS)((PBYTE)pDosHeader +
                                                              pDosHeader->e_lfanew);
            if (pNtHeader->Signature != IMAGE_NT_SIGNATURE) {
                continue;
            }

            return (HMODULE)pDosHeader;
        }
        __except(EXCEPTION_EXECUTE_HANDLER) {
            continue;
        }
    }

    // We only get here via the 'break;' in the loop above, where DETOUR_TRACE_ERROR got called, and LastError was established.
    return NULL;
}

PVOID WINAPI DetourGetEntryPoint(HMODULE hModule)
{
    PIMAGE_DOS_HEADER pDosHeader = (PIMAGE_DOS_HEADER)hModule;
    if (hModule == NULL) {
        pDosHeader = (PIMAGE_DOS_HEADER)GetModuleHandleW(NULL);
    }

    __try {
        if (pDosHeader->e_magic != IMAGE_DOS_SIGNATURE) {
            DETOUR_TRACE_ERROR(L"pDosHeader->e_magic != IMAGE_DOS_SIGNATURE\n");
            SetLastError(ERROR_BAD_EXE_FORMAT);
            return NULL;
        }

        PIMAGE_NT_HEADERS pNtHeader = (PIMAGE_NT_HEADERS)((PBYTE)pDosHeader +
                                                          pDosHeader->e_lfanew);
        if (pNtHeader->Signature != IMAGE_NT_SIGNATURE) {
            DETOUR_TRACE_ERROR(L"pNtHeader->Signature != IMAGE_NT_SIGNATURE\n");
            SetLastError(ERROR_INVALID_EXE_SIGNATURE);
            return NULL;
        }
        if (pNtHeader->FileHeader.SizeOfOptionalHeader == 0) {
            DETOUR_TRACE_ERROR(L"pNtHeader->FileHeader.SizeOfOptionalHeader == 0\n");
            SetLastError(ERROR_EXE_MARKED_INVALID);
            return NULL;
        }

        PDETOUR_CLR_HEADER pClrHeader = NULL;
        if (pNtHeader->OptionalHeader.Magic == IMAGE_NT_OPTIONAL_HDR32_MAGIC) {
            if (((PIMAGE_NT_HEADERS32)pNtHeader)->CLR_DIRECTORY.VirtualAddress != 0 &&
                ((PIMAGE_NT_HEADERS32)pNtHeader)->CLR_DIRECTORY.Size != 0) {
                pClrHeader = (PDETOUR_CLR_HEADER)
                    (((PBYTE)pDosHeader)
                     + ((PIMAGE_NT_HEADERS32)pNtHeader)->CLR_DIRECTORY.VirtualAddress);
            }
        }
        else if (pNtHeader->OptionalHeader.Magic == IMAGE_NT_OPTIONAL_HDR64_MAGIC) {
            if (((PIMAGE_NT_HEADERS64)pNtHeader)->CLR_DIRECTORY.VirtualAddress != 0 &&
                ((PIMAGE_NT_HEADERS64)pNtHeader)->CLR_DIRECTORY.Size != 0) {
                pClrHeader = (PDETOUR_CLR_HEADER)
                    (((PBYTE)pDosHeader)
                     + ((PIMAGE_NT_HEADERS64)pNtHeader)->CLR_DIRECTORY.VirtualAddress);
            }
        }

        if (pClrHeader != NULL) {
            // For MSIL assemblies, we want to use the _Cor entry points.

            HMODULE hClr = GetModuleHandleW(L"MSCOREE.DLL");
            if (hClr == NULL) {
                DETOUR_TRACE_ERROR(L"GetModuleHandleW(MSCOREE.DLL) failed: %d\n", GetLastError());
                return NULL;
            }

            SetLastError(NO_ERROR);
            return GetProcAddress(hClr, "_CorExeMain");
        }

        SetLastError(NO_ERROR);
        return ((PBYTE)pDosHeader) +
            pNtHeader->OptionalHeader.AddressOfEntryPoint;
    }
    __except(EXCEPTION_EXECUTE_HANDLER) {
        DETOUR_TRACE_ERROR(L"EXCEPTION_EXECUTE_HANDLER\n");
        SetLastError(ERROR_EXE_MARKED_INVALID);
        return NULL;
    }
}

ULONG WINAPI DetourGetModuleSize(HMODULE hModule)
{
    PIMAGE_DOS_HEADER pDosHeader = (PIMAGE_DOS_HEADER)hModule;
    if (hModule == NULL) {
        pDosHeader = (PIMAGE_DOS_HEADER)GetModuleHandleW(NULL);
    }

    __try {
        if (pDosHeader->e_magic != IMAGE_DOS_SIGNATURE) {
            DETOUR_TRACE_ERROR(L"pDosHeader->e_magic != IMAGE_DOS_SIGNATURE\n");
            SetLastError(ERROR_BAD_EXE_FORMAT);
            return NULL;
        }

        PIMAGE_NT_HEADERS pNtHeader = (PIMAGE_NT_HEADERS)((PBYTE)pDosHeader +
                                                          pDosHeader->e_lfanew);
        if (pNtHeader->Signature != IMAGE_NT_SIGNATURE) {
            DETOUR_TRACE_ERROR(L"pNtHeader->Signature != IMAGE_NT_SIGNATURE\n");
            SetLastError(ERROR_INVALID_EXE_SIGNATURE);
            return NULL;
        }
        if (pNtHeader->FileHeader.SizeOfOptionalHeader == 0) {
            DETOUR_TRACE_ERROR(L"pNtHeader->FileHeader.SizeOfOptionalHeader == 0\n");
            SetLastError(ERROR_EXE_MARKED_INVALID);
            return NULL;
        }
        SetLastError(NO_ERROR);

        return (pNtHeader->OptionalHeader.SizeOfImage);
    }
    __except(EXCEPTION_EXECUTE_HANDLER) {
        DETOUR_TRACE_ERROR(L"EXCEPTION_EXECUTE_HANDLE\n");
        SetLastError(ERROR_EXE_MARKED_INVALID);
        return NULL;
    }
}

HMODULE WINAPI DetourGetContainingModule(PVOID pvAddr)
{
    MEMORY_BASIC_INFORMATION mbi;
    ZeroMemory(&mbi, sizeof(mbi));

    __try {
        if (VirtualQuery(pvAddr, &mbi, sizeof(mbi)) <= 0) {
            DETOUR_TRACE_ERROR(L"VirtualQuery(%p) failed: %d\n", 
                pvAddr, GetLastError());
            SetLastError(ERROR_BAD_EXE_FORMAT);
            return NULL;
        }

        // Skip uncommitted regions and guard pages.
        //
        if ((mbi.State != MEM_COMMIT) ||
            ((mbi.Protect & 0xff) == PAGE_NOACCESS) ||
            (mbi.Protect & PAGE_GUARD)) {
            DETOUR_TRACE_ERROR(L"Bad state\n");
            SetLastError(ERROR_BAD_EXE_FORMAT);
            return NULL;
        }

        PIMAGE_DOS_HEADER pDosHeader = (PIMAGE_DOS_HEADER)mbi.AllocationBase;
        if (pDosHeader->e_magic != IMAGE_DOS_SIGNATURE) {
            DETOUR_TRACE_ERROR(L"pDosHeader->e_magic != IMAGE_DOS_SIGNATURE\n");
            SetLastError(ERROR_BAD_EXE_FORMAT);
            return NULL;
        }

        PIMAGE_NT_HEADERS pNtHeader = (PIMAGE_NT_HEADERS)((PBYTE)pDosHeader +
                                                          pDosHeader->e_lfanew);
        if (pNtHeader->Signature != IMAGE_NT_SIGNATURE) {
            DETOUR_TRACE_ERROR(L"pNtHeader->Signature != IMAGE_NT_SIGNATURE\n");
            SetLastError(ERROR_INVALID_EXE_SIGNATURE);
            return NULL;
        }
        if (pNtHeader->FileHeader.SizeOfOptionalHeader == 0) {
            DETOUR_TRACE_ERROR(L"pNtHeader->FileHeader.SizeOfOptionalHeader == 0\n");
            SetLastError(ERROR_EXE_MARKED_INVALID);
            return NULL;
        }
        SetLastError(NO_ERROR);

        return (HMODULE)pDosHeader;
    }
    __except(EXCEPTION_EXECUTE_HANDLER) {
        DETOUR_TRACE_ERROR(L"EXCEPTION_EXECUTE_HANDLER\n");
        SetLastError(ERROR_INVALID_EXE_SIGNATURE);
        return NULL;
    }
}


static inline PBYTE RvaAdjust(PIMAGE_DOS_HEADER pDosHeader, DWORD raddr)
{
    if (raddr != NULL) {
        return ((PBYTE)pDosHeader) + raddr;
    }
    return NULL;
}

BOOL WINAPI DetourEnumerateExports(HMODULE hModule,
                                   PVOID pContext,
                                   PF_DETOUR_ENUMERATE_EXPORT_CALLBACK pfExport)
{
    PIMAGE_DOS_HEADER pDosHeader = (PIMAGE_DOS_HEADER)hModule;
    if (hModule == NULL) {
        pDosHeader = (PIMAGE_DOS_HEADER)GetModuleHandleW(NULL);
    }

    __try {
        if (pDosHeader->e_magic != IMAGE_DOS_SIGNATURE) {
            DETOUR_TRACE_ERROR(L"pDosHeader->e_magic != IMAGE_DOS_SIGNATURE\n");
            SetLastError(ERROR_BAD_EXE_FORMAT);
            return NULL;
        }

        PIMAGE_NT_HEADERS pNtHeader = (PIMAGE_NT_HEADERS)((PBYTE)pDosHeader +
                                                          pDosHeader->e_lfanew);
        if (pNtHeader->Signature != IMAGE_NT_SIGNATURE) {
            DETOUR_TRACE_ERROR(L"pNtHeader->Signature != IMAGE_NT_SIGNATURE\n");
            SetLastError(ERROR_INVALID_EXE_SIGNATURE);
            return FALSE;
        }
        if (pNtHeader->FileHeader.SizeOfOptionalHeader == 0) {
            DETOUR_TRACE_ERROR(L"pNtHeader->FileHeader.SizeOfOptionalHeader == 0\n");
            SetLastError(ERROR_EXE_MARKED_INVALID);
            return FALSE;
        }

        PIMAGE_EXPORT_DIRECTORY pExportDir
            = (PIMAGE_EXPORT_DIRECTORY)
            RvaAdjust(pDosHeader,
                      pNtHeader->OptionalHeader
                      .DataDirectory[IMAGE_DIRECTORY_ENTRY_EXPORT].VirtualAddress);

        if (pExportDir == NULL) {
            DETOUR_TRACE_ERROR(L"pExportDir == NULL\n");
            SetLastError(ERROR_EXE_MARKED_INVALID);
            return FALSE;
        }

        PDWORD pdwFunctions = (PDWORD)RvaAdjust(pDosHeader, pExportDir->AddressOfFunctions);
        PDWORD pdwNames = (PDWORD)RvaAdjust(pDosHeader, pExportDir->AddressOfNames);
        PWORD pwOrdinals = (PWORD)RvaAdjust(pDosHeader, pExportDir->AddressOfNameOrdinals);

        for (DWORD nFunc = 0; nFunc < pExportDir->NumberOfFunctions; nFunc++) {
            PBYTE pbCode = (pdwFunctions != NULL)
                ? (PBYTE)RvaAdjust(pDosHeader, pdwFunctions[nFunc]) : NULL;
            PCHAR pszName = NULL;
            for (DWORD n = 0; n < pExportDir->NumberOfNames; n++) {
                if (pwOrdinals[n] == nFunc) {
                    pszName = (pdwNames != NULL)
                        ? (PCHAR)RvaAdjust(pDosHeader, pdwNames[n]) : NULL;
                    break;
                }
            }
            ULONG nOrdinal = pExportDir->Base + nFunc;

            if (!pfExport(pContext, nOrdinal, pszName, pbCode)) {
                break;
            }
        }
        SetLastError(NO_ERROR);
        return TRUE;
    }
    __except(EXCEPTION_EXECUTE_HANDLER) {
        DETOUR_TRACE_ERROR(L"EXCEPTION_EXECUTE_HANDLER\n");
        SetLastError(ERROR_EXE_MARKED_INVALID);
        return NULL;
    }
}

BOOL WINAPI DetourEnumerateImports(HMODULE hModule,
                                   PVOID pContext,
                                   PF_DETOUR_IMPORT_FILE_CALLBACK pfImportFile,
                                   PF_DETOUR_IMPORT_FUNC_CALLBACK pfImportFunc)
{
    PIMAGE_DOS_HEADER pDosHeader = (PIMAGE_DOS_HEADER)hModule;
    if (hModule == NULL) {
        pDosHeader = (PIMAGE_DOS_HEADER)GetModuleHandleW(NULL);
    }

    __try {
        if (pDosHeader->e_magic != IMAGE_DOS_SIGNATURE) {
            DETOUR_TRACE_ERROR(L"pDosHeader->e_magic != IMAGE_DOS_SIGNATURE\n");
            SetLastError(ERROR_BAD_EXE_FORMAT);
            return FALSE;
        }

        PIMAGE_NT_HEADERS pNtHeader = (PIMAGE_NT_HEADERS)((PBYTE)pDosHeader +
                                                          pDosHeader->e_lfanew);
        if (pNtHeader->Signature != IMAGE_NT_SIGNATURE) {
            DETOUR_TRACE_ERROR(L"pNtHeader->Signature != IMAGE_NT_SIGNATURE\n");
            SetLastError(ERROR_INVALID_EXE_SIGNATURE);
            return FALSE;
        }
        if (pNtHeader->FileHeader.SizeOfOptionalHeader == 0) {
            DETOUR_TRACE_ERROR(L"pNtHeader->FileHeader.SizeOfOptionalHeader == 0\n");
            SetLastError(ERROR_EXE_MARKED_INVALID);
            return FALSE;
        }

        PIMAGE_IMPORT_DESCRIPTOR iidp
            = (PIMAGE_IMPORT_DESCRIPTOR)
            RvaAdjust(pDosHeader,
                      pNtHeader->OptionalHeader
                      .DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT].VirtualAddress);

        if (iidp == NULL) {
            DETOUR_TRACE_ERROR(L"iidp == NULL\n");
            SetLastError(ERROR_EXE_MARKED_INVALID);
            return FALSE;
        }

        for (; iidp->OriginalFirstThunk != 0; iidp++) {

            PCSTR pszName = (PCHAR)RvaAdjust(pDosHeader, iidp->Name);
            if (pszName == NULL) {
                DETOUR_TRACE_ERROR(L"pszName == NULL\n");
                SetLastError(ERROR_EXE_MARKED_INVALID);
                return FALSE;
            }

            PIMAGE_THUNK_DATA pThunks = (PIMAGE_THUNK_DATA)
                RvaAdjust(pDosHeader, iidp->OriginalFirstThunk);
            PVOID * pAddrs = (PVOID *)
                RvaAdjust(pDosHeader, iidp->FirstThunk);

            HMODULE hFile = DetourGetContainingModule(pAddrs[0]);

            if (pfImportFile != NULL) {
                if (!pfImportFile(pContext, hFile, pszName)) {
                    break;
                }
            }

            DWORD nNames = 0;
            if (pThunks) {
                for (; pThunks[nNames].u1.Ordinal; nNames++) {
                    DWORD nOrdinal = 0;
                    PCSTR pszFunc = NULL;

                    if (IMAGE_SNAP_BY_ORDINAL(pThunks[nNames].u1.Ordinal)) {
                        nOrdinal = (DWORD)IMAGE_ORDINAL(pThunks[nNames].u1.Ordinal);
                    }
                    else {
                        pszFunc = (PCSTR)RvaAdjust(pDosHeader,
                                                   (DWORD)pThunks[nNames].u1.AddressOfData + 2);
                    }

                    if (pfImportFunc != NULL) {
                        if (!pfImportFunc(pContext,
                                          nOrdinal,
                                          pszFunc,
                                          pAddrs[nNames])) {
                            break;
                        }
                    }
                }
                if (pfImportFunc != NULL) {
                    pfImportFunc(pContext, 0, NULL, NULL);
                }
            }
        }
        if (pfImportFile != NULL) {
            pfImportFile(pContext, NULL, NULL);
        }
        SetLastError(NO_ERROR);
        return TRUE;
    }
    __except(EXCEPTION_EXECUTE_HANDLER) {
        DETOUR_TRACE_ERROR(L"EXCEPTION_EXECUTE_HANDLER\n");
        SetLastError(ERROR_EXE_MARKED_INVALID);
        return FALSE;
    }
}

static PDETOUR_LOADED_BINARY WINAPI GetPayloadSectionFromModule(HMODULE hModule)
{
    PIMAGE_DOS_HEADER pDosHeader = (PIMAGE_DOS_HEADER)hModule;
    if (hModule == NULL) {
        pDosHeader = (PIMAGE_DOS_HEADER)GetModuleHandleW(NULL);
    }

    __try {
        if (pDosHeader->e_magic != IMAGE_DOS_SIGNATURE) {
            DETOUR_TRACE_ERROR(L"pDosHeader->e_magic != IMAGE_DOS_SIGNATURE\n");
            SetLastError(ERROR_BAD_EXE_FORMAT);
            return NULL;
        }

        PIMAGE_NT_HEADERS pNtHeader = (PIMAGE_NT_HEADERS)((PBYTE)pDosHeader +
                                                          pDosHeader->e_lfanew);
        if (pNtHeader->Signature != IMAGE_NT_SIGNATURE) {
            DETOUR_TRACE_ERROR(L"pNtHeader->Signature != IMAGE_NT_SIGNATURE\n");
            SetLastError(ERROR_INVALID_EXE_SIGNATURE);
            return NULL;
        }
        if (pNtHeader->FileHeader.SizeOfOptionalHeader == 0) {
            DETOUR_TRACE_ERROR(L"pNtHeader->FileHeader.SizeOfOptionalHeader == 0\n");
            SetLastError(ERROR_EXE_MARKED_INVALID);
            return NULL;
        }

        PIMAGE_SECTION_HEADER pSectionHeaders
            = (PIMAGE_SECTION_HEADER)((PBYTE)pNtHeader
                                      + sizeof(pNtHeader->Signature)
                                      + sizeof(pNtHeader->FileHeader)
                                      + pNtHeader->FileHeader.SizeOfOptionalHeader);

        for (DWORD n = 0; n < pNtHeader->FileHeader.NumberOfSections; n++) {
            if (strcmp((PCHAR)pSectionHeaders[n].Name, ".detour") == 0) {
                if (pSectionHeaders[n].VirtualAddress == 0 ||
                    pSectionHeaders[n].SizeOfRawData == 0) 
                {
                    DETOUR_TRACE_ERROR(L"missing section header\n");
                    break;
                }

                PBYTE pbData = (PBYTE)pDosHeader + pSectionHeaders[n].VirtualAddress;
                DETOUR_SECTION_HEADER *pHeader = (DETOUR_SECTION_HEADER *)pbData;
                if (pHeader->cbHeaderSize < sizeof(DETOUR_SECTION_HEADER) ||
                    pHeader->nSignature != DETOUR_SECTION_HEADER_SIGNATURE) 
                {
                    DETOUR_TRACE_ERROR(L"bad section header\n");
                    break;
                }

                if (pHeader->nDataOffset == 0) {
                    pHeader->nDataOffset = pHeader->cbHeaderSize;
                }
                SetLastError(NO_ERROR);
                return (PBYTE)pHeader;
            }
        }

        DETOUR_TRACE_VERBOSE(L"could not find section header\n");
        SetLastError(ERROR_EXE_MARKED_INVALID);
        return NULL;
    }
    __except(EXCEPTION_EXECUTE_HANDLER) {
        DETOUR_TRACE_ERROR(L"EXCEPTION_EXECUTE_HANDLER\n");
        SetLastError(ERROR_EXE_MARKED_INVALID);
        return NULL;
    }
}

DWORD WINAPI DetourGetSizeOfPayloads(HMODULE hModule)
{
    PDETOUR_LOADED_BINARY pBinary = GetPayloadSectionFromModule(hModule);
    if (pBinary == NULL) {
        // Error set by GetPayloadSectionFromModule.
        DETOUR_TRACE_VERBOSE(L"GetPayloadSectionFromModule(%p) failed: %d\n",
            hModule, GetLastError());
        return 0;
    }

    __try {
        DETOUR_SECTION_HEADER *pHeader = (DETOUR_SECTION_HEADER *)pBinary;
        if (pHeader->cbHeaderSize < sizeof(DETOUR_SECTION_HEADER) ||
            pHeader->nSignature != DETOUR_SECTION_HEADER_SIGNATURE) {

            DETOUR_TRACE_ERROR(L"bad header\n");
            SetLastError(ERROR_INVALID_HANDLE);
            return 0;
        }
        SetLastError(NO_ERROR);
        return pHeader->cbDataSize;
    }
    __except(EXCEPTION_EXECUTE_HANDLER) {
        DETOUR_TRACE_ERROR(L"EXCEPTION_EXECUTE_HANDLER\n");
        SetLastError(ERROR_INVALID_HANDLE);
        return 0;
    }
}

PVOID WINAPI DetourFindPayload(HMODULE hModule, REFGUID rguid, DWORD * pcbData)
{
    PBYTE pbData = NULL;
    if (pcbData) {
        *pcbData = 0;
    }

    PDETOUR_LOADED_BINARY pBinary = GetPayloadSectionFromModule(hModule);
    if (pBinary == NULL) {
        // Error set by GetPayloadSectionFromModule.
        DETOUR_TRACE_VERBOSE(L"GetPayloadSectionFromModule(%p) failed: %d\n",
            hModule, GetLastError());
        return NULL;
    }

    __try {
        DETOUR_SECTION_HEADER *pHeader = (DETOUR_SECTION_HEADER *)pBinary;
        if (pHeader->cbHeaderSize < sizeof(DETOUR_SECTION_HEADER) ||
            pHeader->nSignature != DETOUR_SECTION_HEADER_SIGNATURE) {

            DETOUR_TRACE_ERROR(L"bad header\n");
            SetLastError(ERROR_INVALID_EXE_SIGNATURE);
            return NULL;
        }

        PBYTE pbBeg = ((PBYTE)pHeader) + pHeader->nDataOffset;
        PBYTE pbEnd = ((PBYTE)pHeader) + pHeader->cbDataSize;

        for (pbData = pbBeg; pbData < pbEnd;) {
            DETOUR_SECTION_RECORD *pSection = (DETOUR_SECTION_RECORD *)pbData;

            if (pSection->guid.Data1 == rguid.Data1 &&
                pSection->guid.Data2 == rguid.Data2 &&
                pSection->guid.Data3 == rguid.Data3 &&
                pSection->guid.Data4[0] == rguid.Data4[0] &&
                pSection->guid.Data4[1] == rguid.Data4[1] &&
                pSection->guid.Data4[2] == rguid.Data4[2] &&
                pSection->guid.Data4[3] == rguid.Data4[3] &&
                pSection->guid.Data4[4] == rguid.Data4[4] &&
                pSection->guid.Data4[5] == rguid.Data4[5] &&
                pSection->guid.Data4[6] == rguid.Data4[6] &&
                pSection->guid.Data4[7] == rguid.Data4[7]) {

                if (pcbData) {
                    *pcbData = pSection->cbBytes - sizeof(*pSection);
                }
                SetLastError(NO_ERROR);
                return (PBYTE)(pSection + 1);
            }

            pbData = (PBYTE)pSection + pSection->cbBytes;
        }

        DETOUR_TRACE_VERBOSE(L"could not find section\n");
        SetLastError(ERROR_INVALID_HANDLE);
        return NULL;
    }
    __except(EXCEPTION_EXECUTE_HANDLER) {
        DETOUR_TRACE_ERROR(L"EXCEPTION_EXECUTE_HANDLER\n");
        SetLastError(ERROR_INVALID_HANDLE);
        return NULL;
    }
}

PVOID WINAPI DetourFindPayloadEx(REFGUID rguid, DWORD * pcbData)
{
    for (HMODULE hMod = NULL; (hMod = DetourEnumerateModules(hMod)) != NULL;) {
        PVOID pvData;

        pvData = DetourFindPayload(hMod, rguid, pcbData);
        if (pvData != NULL) {
            return pvData;
        }
    }

    DETOUR_TRACE_ERROR(L"Could not find detour payload\n");
    SetLastError(ERROR_MOD_NOT_FOUND);
    return NULL;
}

BOOL WINAPI DetourRestoreAfterWithEx(PVOID pvData, DWORD cbData)
{
    PDETOUR_EXE_RESTORE pder = (PDETOUR_EXE_RESTORE)pvData;

    if (pder->cb != sizeof(*pder) || pder->cb > cbData) {
        DETOUR_TRACE_ERROR(L"pder->cb != sizeof(*pder) || pder->cb > cbData\n");
        SetLastError(ERROR_BAD_EXE_FORMAT);
        return FALSE;
    }

    DWORD dwPermIdh = ~0u;
    DWORD dwPermInh = ~0u;
    DWORD dwPermClr = ~0u;
    DWORD dwIgnore;
    BOOL fSucceeded = FALSE;

#if 0
    if (pder->pclr != NULL && pder->clr.Flags != ((PDETOUR_CLR_HEADER)pder->pclr)->Flags) {
        // If we had to promote the 32/64-bit agnostic IL to 64-bit, we don't want
        // to restore its IAT.
        __debugbreak();
        return TRUE;
    }
#endif

    if (((pder->pclr != NULL) && ((pder->clr.Flags & 2) == 0)) && // 32BIT Required MSIL is not set
        (pder->inh32.FileHeader.Machine == IMAGE_FILE_MACHINE_I386) &&
        (pder->inh32.OptionalHeader.Magic == IMAGE_NT_OPTIONAL_HDR32_MAGIC)) { // Original Magic is 32 bit

        IMAGE_NT_HEADERS64 *inh64 = (IMAGE_NT_HEADERS64 *)pder->pinh;

        if (((inh64->FileHeader.Characteristics & IMAGE_FILE_DLL) == 0) &&
            (inh64->OptionalHeader.Magic == IMAGE_NT_OPTIONAL_HDR64_MAGIC) &&
            (inh64->FileHeader.Machine == IMAGE_FILE_MACHINE_AMD64)) {

            // If we had to promote the 32/64-bit agnostic IL to 64-bit, we don't
            // want to restore its IAT. 
            // 1. For consistency with LdrpCorFixupImage, Set FileHeader.Machine
            // back to IMAGE_FILE_MACHINE_I386 here.
            // 2. Restore DataDirectory.
            // 3. Restore CLR header
            if (VirtualProtect(pder->pinh, pder->cbinh,
                PAGE_EXECUTE_READWRITE, &dwPermInh)) {

                inh64->FileHeader.Machine = IMAGE_FILE_MACHINE_I386;
                for (DWORD n = 0; n < IMAGE_NUMBEROF_DIRECTORY_ENTRIES; n++) {
                    inh64->OptionalHeader.DataDirectory[n].VirtualAddress = pder->inh32.OptionalHeader.DataDirectory[n].VirtualAddress;
                    inh64->OptionalHeader.DataDirectory[n].Size = pder->inh32.OptionalHeader.DataDirectory[n].Size;
                }

                if (VirtualProtect(pder->pclr, pder->cbclr,
                    PAGE_EXECUTE_READWRITE, &dwPermClr)) {
                    CopyMemory(pder->pclr, &pder->clr, pder->cbclr);
                    VirtualProtect(pder->pclr, pder->cbclr, dwPermClr, &dwIgnore);
                    fSucceeded = TRUE;
                }

                VirtualProtect(pder->pinh, pder->cbinh, dwPermInh, &dwIgnore);
            }

            return fSucceeded;
        }
    }

    if (VirtualProtect(pder->pidh, pder->cbidh,
                       PAGE_EXECUTE_READWRITE, &dwPermIdh)) {
        if (VirtualProtect(pder->pinh, pder->cbinh,
                           PAGE_EXECUTE_READWRITE, &dwPermInh)) {

            CopyMemory(pder->pidh, &pder->idh, pder->cbidh);
            CopyMemory(pder->pinh, &pder->inh, pder->cbinh);

            if (pder->pclr != NULL) {
                if (VirtualProtect(pder->pclr, pder->cbclr,
                                   PAGE_EXECUTE_READWRITE, &dwPermClr)) {
                    CopyMemory(pder->pclr, &pder->clr, pder->cbclr);
                    VirtualProtect(pder->pclr, pder->cbclr, dwPermClr, &dwIgnore);
                    fSucceeded = TRUE;
                }
            }
            else {
                fSucceeded = TRUE;
            }
            VirtualProtect(pder->pinh, pder->cbinh, dwPermInh, &dwIgnore);
        }
        VirtualProtect(pder->pidh, pder->cbidh, dwPermIdh, &dwIgnore);
    }
    return fSucceeded;
}

BOOL WINAPI DetourRestoreAfterWith()
{
    PVOID pvData;
    DWORD cbData;

    pvData = DetourFindPayloadEx(DETOUR_EXE_RESTORE_GUID, &cbData);
    if (pvData == NULL || cbData == 0)
    {
        DETOUR_TRACE_ERROR(L"pvData == NULL || cbData == 0\n");
        SetLastError(ERROR_MOD_NOT_FOUND);
        return FALSE;
    }

    if (!DetourRestoreAfterWithEx(pvData, cbData))
    {
        DETOUR_TRACE_ERROR(L"DetourRestoreAfterWithEx failed: %d\n", GetLastError());
        return FALSE;
    }

    return TRUE;
}

//  End of File
