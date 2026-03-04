// Lightweight native helpers focused on FILE_LEVEL_TRIM. VSS removal is NOT implemented here
// (requires COM/VSS components and admin); we only expose TRIM wrappers suitable for P/Invoke.
// Build Target: Ripcord.Engine.Native (same project as earlier native file).
//
// Exports:
//  - BOOL RcFileLevelTrimW(LPCWSTR pathFile)
//  - BOOL RcFileLevelTrimA(LPCSTR  pathFile)

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <winioctl.h>
#include <stdint.h>

extern "C" {

// Best-effort TRIM for the file at 'pathFile'.
// Returns nonzero on success. Works only when FS/driver supports FILE_LEVEL_TRIM.
__declspec(dllexport) BOOL __stdcall RcFileLevelTrimW(LPCWSTR pathFile)
{
    if (!pathFile || !*pathFile) { SetLastError(ERROR_INVALID_PARAMETER); return FALSE; }

    HANDLE h = CreateFileW(
        pathFile,
        GENERIC_READ | GENERIC_WRITE,
        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
        nullptr,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        nullptr);

    if (h == INVALID_HANDLE_VALUE) return FALSE;

    LARGE_INTEGER size{};
    if (!GetFileSizeEx(h, &size) || size.QuadPart <= 0)
    {
        CloseHandle(h);
        SetLastError(ERROR_INVALID_PARAMETER);
        return FALSE;
    }

    FILE_LEVEL_TRIM_RANGE range{};
    range.Offset = 0;
    range.Length = (ULONGLONG)size.QuadPart;

    FILE_LEVEL_TRIM trim{};
    trim.NumRanges = 1;
    trim.Ranges[0] = range;

    DWORD bytesReturned = 0;
    BOOL ok = DeviceIoControl(
        h,
        FSCTL_FILE_LEVEL_TRIM,
        &trim,
        sizeof(trim),
        nullptr,
        0,
        &bytesReturned,
        nullptr);

    // No fallback here; caller may delete file anyway after issuing TRIM.
    CloseHandle(h);
    return ok;
}

__declspec(dllexport) BOOL __stdcall RcFileLevelTrimA(LPCSTR pathFileA)
{
    if (!pathFileA || !*pathFileA) { SetLastError(ERROR_INVALID_PARAMETER); return FALSE; }

    int wlen = MultiByteToWideChar(CP_UTF8, 0, pathFileA, -1, nullptr, 0);
    if (wlen <= 0) return FALSE;
    WCHAR* w = (WCHAR*)HeapAlloc(GetProcessHeap(), 0, wlen * sizeof(WCHAR));
    if (!w) return FALSE;

    MultiByteToWideChar(CP_UTF8, 0, pathFileA, -1, w, wlen);
    BOOL res = RcFileLevelTrimW(w);
    HeapFree(GetProcessHeap(), 0, w);
    return res;
}

} // extern "C"
