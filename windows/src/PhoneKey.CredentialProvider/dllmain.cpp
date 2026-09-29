#include "common.h"
#include "PhoneKeyCredentialProvider.h"

LONG g_cRef = 0;
HINSTANCE g_hInst = nullptr;

void DllAddRef()
{
    InterlockedIncrement(&g_cRef);
}

void DllRelease()
{
    InterlockedDecrement(&g_cRef);
}

class CClassFactory : public IClassFactory
{
public:
    IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv)
    {
        static const QITAB qit[] =
        {
            QITABENT(CClassFactory, IClassFactory),
            { 0 },
        };
        return QISearch(this, qit, riid, ppv);
    }

    IFACEMETHODIMP_(ULONG) AddRef() { return InterlockedIncrement(&_cRef); }
    IFACEMETHODIMP_(ULONG) Release()
    {
        LONG cRef = InterlockedDecrement(&_cRef);
        if (cRef == 0) delete this;
        return cRef;
    }

    IFACEMETHODIMP CreateInstance(IUnknown* pUnkOuter, REFIID riid, void** ppv)
    {
        if (pUnkOuter) return CLASS_E_NOAGGREGATION;

        CPhoneKeyCredentialProvider* pProvider = new (std::nothrow) CPhoneKeyCredentialProvider();
        if (!pProvider) return E_OUTOFMEMORY;

        HRESULT hr = pProvider->QueryInterface(riid, ppv);
        pProvider->Release();
        return hr;
    }

    IFACEMETHODIMP LockServer(BOOL bLock)
    {
        if (bLock) DllAddRef();
        else DllRelease();
        return S_OK;
    }

    CClassFactory() : _cRef(1) { DllAddRef(); }
    ~CClassFactory() { DllRelease(); }

private:
    LONG _cRef;
};

BOOL WINAPI DllMain(HINSTANCE hinstDLL, DWORD fdwReason, LPVOID)
{
    if (fdwReason == DLL_PROCESS_ATTACH)
    {
        g_hInst = hinstDLL;
        DisableThreadLibraryCalls(hinstDLL);
    }
    return TRUE;
}

STDAPI DllCanUnloadNow()
{
    return (g_cRef == 0) ? S_OK : S_FALSE;
}

STDAPI DllGetClassObject(REFCLSID rclsid, REFIID riid, void** ppv)
{
    if (!ppv) return E_INVALIDARG;
    *ppv = nullptr;

    if (rclsid == CLSID_PhoneKeyCredentialProvider)
    {
        CClassFactory* pFactory = new (std::nothrow) CClassFactory();
        if (!pFactory) return E_OUTOFMEMORY;

        HRESULT hr = pFactory->QueryInterface(riid, ppv);
        pFactory->Release();
        return hr;
    }

    return CLASS_E_CLASSNOTAVAILABLE;
}

STDAPI DllRegisterServer()
{
    WCHAR szModule[MAX_PATH];
    if (!GetModuleFileNameW(g_hInst, szModule, ARRAYSIZE(szModule)))
    {
        return HRESULT_FROM_WIN32(GetLastError());
    }

    // Register CLSID under HKCR\CLSID\{8E79B5A2-7D3C-4D2A-98C1-F04E51C2D901}
    HKEY hKey = nullptr;
    WCHAR szClsidKey[256];
    StringCchPrintfW(szClsidKey, ARRAYSIZE(szClsidKey), 
        L"CLSID\\{8E79B5A2-7D3C-4D2A-98C1-F04E51C2D901}");

    if (RegCreateKeyExW(HKEY_CLASSES_ROOT, szClsidKey, 0, nullptr, 0, KEY_WRITE, nullptr, &hKey, nullptr) == ERROR_SUCCESS)
    {
        RegSetValueExW(hKey, nullptr, 0, REG_SZ, (const BYTE*)L"PhoneKey Credential Provider", (DWORD)(sizeof(L"PhoneKey Credential Provider")));
        
        HKEY hInproc = nullptr;
        if (RegCreateKeyExW(hKey, L"InprocServer32", 0, nullptr, 0, KEY_WRITE, nullptr, &hInproc, nullptr) == ERROR_SUCCESS)
        {
            RegSetValueExW(hInproc, nullptr, 0, REG_SZ, (const BYTE*)szModule, (DWORD)((wcslen(szModule) + 1) * sizeof(WCHAR)));
            RegSetValueExW(hInproc, L"ThreadingModel", 0, REG_SZ, (const BYTE*)L"Apartment", (DWORD)(sizeof(L"Apartment")));
            RegCloseKey(hInproc);
        }
        RegCloseKey(hKey);
    }

    // Register under HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers
    HKEY hCpKey = nullptr;
    WCHAR szCpRegPath[256];
    StringCchPrintfW(szCpRegPath, ARRAYSIZE(szCpRegPath),
        L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Authentication\\Credential Providers\\{8E79B5A2-7D3C-4D2A-98C1-F04E51C2D901}");

    if (RegCreateKeyExW(HKEY_LOCAL_MACHINE, szCpRegPath, 0, nullptr, 0, KEY_WRITE, nullptr, &hCpKey, nullptr) == ERROR_SUCCESS)
    {
        RegSetValueExW(hCpKey, nullptr, 0, REG_SZ, (const BYTE*)L"PhoneKeyCredentialProvider", (DWORD)(sizeof(L"PhoneKeyCredentialProvider")));
        RegCloseKey(hCpKey);
    }

    return S_OK;
}

STDAPI DllUnregisterServer()
{
    WCHAR szCpRegPath[256];
    StringCchPrintfW(szCpRegPath, ARRAYSIZE(szCpRegPath),
        L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Authentication\\Credential Providers\\{8E79B5A2-7D3C-4D2A-98C1-F04E51C2D901}");
    RegDeleteKeyW(HKEY_LOCAL_MACHINE, szCpRegPath);

    WCHAR szClsidInproc[256];
    StringCchPrintfW(szClsidInproc, ARRAYSIZE(szClsidInproc),
        L"CLSID\\{8E79B5A2-7D3C-4D2A-98C1-F04E51C2D901}\\InprocServer32");
    RegDeleteKeyW(HKEY_CLASSES_ROOT, szClsidInproc);

    WCHAR szClsidKey[256];
    StringCchPrintfW(szClsidKey, ARRAYSIZE(szClsidKey),
        L"CLSID\\{8E79B5A2-7D3C-4D2A-98C1-F04E51C2D901}");
    RegDeleteKeyW(HKEY_CLASSES_ROOT, szClsidKey);

    return S_OK;
}
