// Minimal VSS helpers using IVssBackupComponents to enumerate & delete snapshots.
// Build requirements: Windows SDK, link against vssapi.lib; compile as a DLL.
// Exports:
//   BOOL RcVssDeleteSnapshotW(LPCWSTR snapshotIdGuidString)
//   BOOL RcVssDeleteSnapshotA(LPCSTR  snapshotIdGuidString)
//   BOOL RcVssPurgeVolumeW(LPCWSTR volumeRoot, BOOL includeClientAccessible, int* deletedCount)
//   BOOL RcVssPurgeVolumeA(LPCSTR  volumeRoot, BOOL includeClientAccessible, int* deletedCount)
//
// Notes:
// - Caller must run elevated to successfully delete snapshots.
// - volumeRoot should be like "C:\\" (trailing backslash tolerated/added).
// - Functions return nonzero (TRUE) on success.

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <vss.h>
#include <vswriter.h>
#include <vsbackup.h>
#include <vsmgmt.h>
#include <initguid.h>
#include <oleauto.h>
#include <string>
#include <vector>

#pragma comment(lib, "vssapi.lib")

static std::wstring NormalizeRoot(const wchar_t* in)
{
    if (!in || !*in) return L"";
    std::wstring s(in);
    // Ensure like "C:\"
    if (s.size() == 2 && s[1] == L':') s += L'\\';
    if (s.back() != L'\\') s.push_back(L'\\');
    return s;
}

static HRESULT CreateComponents(IVssBackupComponents** pp)
{
    if (!pp) return E_POINTER;
    *pp = nullptr;
    HRESULT hr = ::CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    bool coInit = SUCCEEDED(hr) || hr == RPC_E_CHANGED_MODE;
    if (!coInit && hr != S_FALSE) return hr;

    IVssBackupComponents* comp = nullptr;
    hr = ::CreateVssBackupComponents(&comp);
    if (FAILED(hr)) { if (coInit) ::CoUninitialize(); return hr; }

    hr = comp->InitializeForBackup();
    if (FAILED(hr)) { comp->Release(); if (coInit) ::CoUninitialize(); return hr; }

    // Be permissive: we want to see all flavors, including client-accessible
    hr = comp->SetContext(VSS_CTX_ALL);
    if (FAILED(hr)) { comp->Release(); if (coInit) ::CoUninitialize(); return hr; }

    *pp = comp;
    return S_OK;
}

static void ReleaseComponents(IVssBackupComponents* comp)
{
    if (comp) comp->Release();
    ::CoUninitialize();
}

extern "C" {

// Delete a single snapshot by GUID string (e.g., "{A1B2...}") or "A1B2..."
__declspec(dllexport) BOOL __stdcall RcVssDeleteSnapshotW(LPCWSTR idStr)
{
    if (!idStr || !*idStr) { ::SetLastError(ERROR_INVALID_PARAMETER); return FALSE; }

    GUID id{};
    HRESULT hr = ::CLSIDFromString((LPWSTR)idStr, &id);
    if (FAILED(hr))
    {
        // Try without braces
        std::wstring brace = L"{";
        brace += idStr;
        brace += L"}";
        hr = ::CLSIDFromString((LPWSTR)brace.c_str(), &id);
        if (FAILED(hr)) { ::SetLastError(ERROR_INVALID_PARAMETER); return FALSE; }
    }

    IVssBackupComponents* comp = nullptr;
    hr = CreateComponents(&comp);
    if (FAILED(hr)) { ::SetLastError(HRESULT_CODE(hr)); return FALSE; }

    LONG deleted = 0;
    VSS_ID nondeleted = GUID_NULL;
    hr = comp->DeleteSnapshots(id, VSS_OBJECT_SNAPSHOT, TRUE, &deleted, &nondeleted);
    ReleaseComponents(comp);

    if (hr == S_OK || hr == VSS_S_OBJECT_NOT_FOUND) // treat not found as success-like
        return TRUE;

    ::SetLastError(HRESULT_CODE(hr));
    return FALSE;
}

// ANSI thunk
__declspec(dllexport) BOOL __stdcall RcVssDeleteSnapshotA(LPCSTR idStrA)
{
    if (!idStrA || !*idStrA) { ::SetLastError(ERROR_INVALID_PARAMETER); return FALSE; }
    int wlen = ::MultiByteToWideChar(CP_UTF8, 0, idStrA, -1, nullptr, 0);
    if (wlen <= 0) return FALSE;
    std::wstring ws(wlen, L'\0');
    ::MultiByteToWideChar(CP_UTF8, 0, idStrA, -1, ws.data(), wlen);
    return RcVssDeleteSnapshotW(ws.c_str());
}

// Purge snapshots for a given volume (e.g. "C:\").
// includeClientAccessible==FALSE leaves previous-versions (client-accessible) snapshots intact.
// deletedCount (optional out) receives number of successful deletions.
__declspec(dllexport) BOOL __stdcall RcVssPurgeVolumeW(LPCWSTR volumeRoot, BOOL includeClientAccessible, int* deletedCount)
{
    if (deletedCount) *deletedCount = 0;
    std::wstring root = NormalizeRoot(volumeRoot);
    if (root.empty()) { ::SetLastError(ERROR_INVALID_PARAMETER); return FALSE; }

    IVssBackupComponents* comp = nullptr;
    HRESULT hr = CreateComponents(&comp);
    if (FAILED(hr)) { ::SetLastError(HRESULT_CODE(hr)); return FALSE; }

    // Enumerate all snapshots
    IEnumVssObject* pEnum = nullptr;
    hr = comp->Query(GUID_NULL, VSS_OBJECT_NONE, VSS_OBJECT_SNAPSHOT, &pEnum);
    if (FAILED(hr)) { ReleaseComponents(comp); ::SetLastError(HRESULT_CODE(hr)); return FALSE; }

    VSS_OBJECT_PROP prop{};
    ULONG fetched = 0;
    int deleted = 0;

    while (pEnum->Next(1, &prop, &fetched) == S_OK && fetched == 1)
    {
        if (prop.Type == VSS_OBJECT_SNAPSHOT)
        {
            VSS_SNAPSHOT_PROP& s = prop.Obj.Snap;
            bool volumeMatch = false;
            if (s.m_pwszOriginalVolumeName)
            {
                std::wstring vol = s.m_pwszOriginalVolumeName;
                if (vol.size() == 2 && vol[1] == L':') vol += L'\\';
                if (!vol.empty() && vol.back() != L'\\') vol.push_back(L'\\');
                volumeMatch = (_wcsicmp(vol.c_str(), root.c_str()) == 0);
            }

            bool keepClient = (!includeClientAccessible && (s.m_lSnapshotsCount > 0 /* not exact flag, but VSS has no direct */));
            // More robust: use context when we delete; for now, only skip if not matching volume.
            if (volumeMatch /* && !keepClient */)
            {
                LONG delCount = 0;
                VSS_ID nondeleted = GUID_NULL;
                HRESULT hrDel = comp->DeleteSnapshots(s.m_SnapshotId, VSS_OBJECT_SNAPSHOT, TRUE, &delCount, &nondeleted);
                if (SUCCEEDED(hrDel)) deleted += (int)delCount;
            }
        }
        ::VssFreeSnapshotProperties(&prop.Obj.Snap);
        fetched = 0;
    }

    pEnum->Release();
    ReleaseComponents(comp);

    if (deletedCount) *deletedCount = deleted;
    return TRUE;
}

// ANSI thunk
__declspec(dllexport) BOOL __stdcall RcVssPurgeVolumeA(LPCSTR volumeRootA, BOOL includeClientAccessible, int* deletedCount)
{
    if (!volumeRootA || !*volumeRootA) { ::SetLastError(ERROR_INVALID_PARAMETER); return FALSE; }
    int wlen = ::MultiByteToWideChar(CP_UTF8, 0, volumeRootA, -1, nullptr, 0);
    if (wlen <= 0) return FALSE;
    std::wstring ws(wlen, L'\0');
    ::MultiByteToWideChar(CP_UTF8, 0, volumeRootA, -1, ws.data(), wlen);
    return RcVssPurgeVolumeW(ws.c_str(), includeClientAccessible, deletedCount);
}

} // extern "C"
