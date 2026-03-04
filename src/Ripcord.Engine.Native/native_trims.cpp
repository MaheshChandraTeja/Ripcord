// Minimal native helpers for file-level TRIM / zero-range operations on Windows.
// Exported C functions are suitable for P/Invoke from .NET.
//
// Notes:
// - FSCTL_FILE_LEVEL_TRIM is best-effort and may require admin or specific FS.
// - We fall back to FSCTL_SET_ZERO_DATA if TRIM is not supported.
// - These routines do NOT replace the secure-overwrite pipeline; they provide
//   additional hints to the storage stack after shredding, to encourage media
//   deallocation on SSDs.
//
// Build: part of Ripcord.Engine.Native (x64/ARM64)

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <winioctl.h>
#include <stdint.h>

extern "C" {

// Returns nonzero on success. Best-effort; will return 0 if not supported.
__declspec(dllexport) BOOL __stdcall RcFileLevelTrimW(LPCWSTR path)
{
    if (!path || !*path) return FALSE;

    HANDLE h = CreateFileW(
        path,
        GENERIC_READ | GENERIC_WRITE,
        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
        nullptr,
        OPEN_EXISTING,
        FILE_FLAG_BACKUP_SEMANTICS, // allow directories too
        nullptr);

    if (h == INVALID_HANDLE_VALUE)
        return FALSE;

    LARGE_INTEGER size{};
    BOOL ok = GetFileSizeEx(h, &size);
    if (!ok || size.QuadPart <= 0)
    {
        CloseHandle(h);
        SetLastError(ok ? ERROR_INVALID_PARAMETER : GetLastError());
        return FALSE;
    }

    // Prepare a single-range TRIM for [0, size)
    FILE_LEVEL_TRIM_RANGE range{};
    range.Offset = 0;
    range.Length = (ULONGLONG)size.QuadPart;

    FILE_LEVEL_TRIM trim{};
    trim.NumRanges = 1;
    trim.Ranges[0] = range;

    DWORD bytesReturned = 0;
    ok = DeviceIoControl(
        h,
        FSCTL_FILE_LEVEL_TRIM,
        &trim,
        sizeof(trim),
        nullptr,
        0,
        &bytesReturned,
        nullptr);

    DWORD err = GetLastError();

    // If TRIM fails, try zero-range (which can deallocate on sparse files)
    if (!ok)
    {
        FILE_ZERO_DATA_INFORMATION zero{};
        zero.FileOffset.QuadPart = 0;
        zero.BeyondFinalZero.QuadPart = size.QuadPart;

        ok = DeviceIoControl(
            h,
            FSCTL_SET_ZERO_DATA,
            &zero,
            sizeof(zero),
            nullptr,
            0,
            &bytesReturned,
            nullptr);

        if (!ok)
        {
            // If that also failed, try marking sparse + zero-range
            // (punch holes where possible)
            DWORD tmp = 0;
            BOOL spOk = DeviceIoControl(h, FSCTL_SET_SPARSE, nullptr, 0, nullptr, 0, &tmp, nullptr);
            if (spOk)
            {
                ok = DeviceIoControl(
                    h,
                    FSCTL_SET_ZERO_DATA,
                    &zero,
                    sizeof(zero),
                    nullptr,
                    0,
                    &bytesReturned,
                    nullptr);
            }
        }
    }

    CloseHandle(h);
    if (!ok && err != 0) SetLastError(err);
    return ok;
}

// Convenience ANSI thunk
__declspec(dllexport) BOOL __stdcall RcFileLevelTrimA(LPCSTR pathA)
{
    if (!pathA || !*pathA) return FALSE;
    int wlen = MultiByteToWideChar(CP_UTF8, 0, pathA, -1, nullptr, 0);
    if (wlen <= 0) return FALSE;
    WCHAR* w = (WCHAR*)HeapAlloc(GetProcessHeap(), 0, wlen * sizeof(WCHAR));
    if (!w) return FALSE;
    MultiByteToWideChar(CP_UTF8, 0, pathA, -1, w, wlen);
    BOOL res = RcFileLevelTrimW(w);
    HeapFree(GetProcessHeap(), 0, w);
    return res;
}

} // extern "C"
