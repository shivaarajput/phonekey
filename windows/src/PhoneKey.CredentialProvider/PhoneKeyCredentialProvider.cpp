#include "PhoneKeyCredentialProvider.h"
#include "IpcClient.h"

// Field definitions for LogonUI display
static const CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR s_rgFieldDescriptors[] =
{
    { PKFI_TILEIMAGE,     CPFT_TILE_IMAGE,    const_cast<PWSTR>(L"") },
    { PKFI_LABEL,         CPFT_LARGE_TEXT,    const_cast<PWSTR>(L"PhoneKey") },
    { PKFI_SUBMIT_BUTTON, CPFT_SUBMIT_BUTTON, const_cast<PWSTR>(L"Unlock") },
    { PKFI_STATUS_TEXT,   CPFT_SMALL_TEXT,    const_cast<PWSTR>(L"") },
};

CPhoneKeyCredentialProvider::CPhoneKeyCredentialProvider() :
    _cRef(1),
    _cpus(CPUS_LOGON),
    _pEvents(nullptr),
    _upAdviseContext(0),
    _pCredential(nullptr)
{
    DllAddRef();
}

CPhoneKeyCredentialProvider::~CPhoneKeyCredentialProvider()
{
    if (_pCredential)
    {
        _pCredential->Release();
        _pCredential = nullptr;
    }
    if (_pEvents)
    {
        _pEvents->Release();
        _pEvents = nullptr;
    }
    DllRelease();
}

// IUnknown
IFACEMETHODIMP CPhoneKeyCredentialProvider::QueryInterface(REFIID riid, void** ppv)
{
    static const QITAB qit[] =
    {
        QITABENT(CPhoneKeyCredentialProvider, ICredentialProvider),
        { 0 },
    };
    return QISearch(this, qit, riid, ppv);
}

IFACEMETHODIMP_(ULONG) CPhoneKeyCredentialProvider::AddRef()
{
    return InterlockedIncrement(&_cRef);
}

IFACEMETHODIMP_(ULONG) CPhoneKeyCredentialProvider::Release()
{
    LONG cRef = InterlockedDecrement(&_cRef);
    if (cRef == 0)
    {
        delete this;
    }
    return cRef;
}

// ICredentialProvider
IFACEMETHODIMP CPhoneKeyCredentialProvider::SetUsageScenario(
    CREDENTIAL_PROVIDER_USAGE_SCENARIO cpus, 
    DWORD)
{
    switch (cpus)
    {
    case CPUS_LOGON:
    case CPUS_UNLOCK_WORKSTATION:
        _cpus = cpus;
        return S_OK;

    case CPUS_CHANGE_PASSWORD:
    default:
        return E_NOTIMPL;
    }
}

IFACEMETHODIMP CPhoneKeyCredentialProvider::SetSerialization(const CREDENTIAL_PROVIDER_CREDENTIAL_SERIALIZATION*)
{
    return E_NOTIMPL;
}

IFACEMETHODIMP CPhoneKeyCredentialProvider::Advise(ICredentialProviderEvents* pEvents, UINT_PTR upAdviseContext)
{
    if (_pEvents)
    {
        _pEvents->Release();
    }
    _pEvents = pEvents;
    _upAdviseContext = upAdviseContext;
    if (_pEvents)
    {
        _pEvents->AddRef();
    }
    return S_OK;
}

IFACEMETHODIMP CPhoneKeyCredentialProvider::UnAdvise()
{
    if (_pEvents)
    {
        _pEvents->Release();
        _pEvents = nullptr;
    }
    _upAdviseContext = 0;
    return S_OK;
}

IFACEMETHODIMP CPhoneKeyCredentialProvider::GetFieldDescriptorCount(DWORD* pdwCount)
{
    if (!pdwCount) return E_INVALIDARG;
    *pdwCount = PKFI_NUM_FIELDS;
    return S_OK;
}

IFACEMETHODIMP CPhoneKeyCredentialProvider::GetFieldDescriptorAt(
    DWORD dwIndex, 
    CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR** ppcpfd)
{
    if (!ppcpfd || dwIndex >= PKFI_NUM_FIELDS) return E_INVALIDARG;

    CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR* pcpfd = 
        (CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR*)CoTaskMemAlloc(sizeof(CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR));
    if (!pcpfd) return E_OUTOFMEMORY;

    pcpfd->dwFieldID = s_rgFieldDescriptors[dwIndex].dwFieldID;
    pcpfd->cpft = s_rgFieldDescriptors[dwIndex].cpft;
    SHStrDupW(s_rgFieldDescriptors[dwIndex].pszLabel, &pcpfd->pszLabel);

    *ppcpfd = pcpfd;
    return S_OK;
}

IFACEMETHODIMP CPhoneKeyCredentialProvider::GetCredentialCount(
    DWORD* pdwCount, 
    DWORD* pdwDefault, 
    BOOL* pbAutoLogonWithDefault)
{
    if (!pdwCount || !pdwDefault || !pbAutoLogonWithDefault) return E_INVALIDARG;

    *pdwCount = 1;
    *pdwDefault = 0;

    PhoneKeyStatus status;
    ZeroMemory(&status, sizeof(status));
    CIpcClient::QueryStatus(&status);

    *pbAutoLogonWithDefault = (status.bInProximity && status.bAuthenticated);
    return S_OK;
}

IFACEMETHODIMP CPhoneKeyCredentialProvider::GetCredentialAt(DWORD dwIndex, ICredentialProviderCredential** ppcpc)
{
    if (!ppcpc || dwIndex != 0) return E_INVALIDARG;

    if (!_pCredential)
    {
        _pCredential = new (std::nothrow) CPhoneKeyCredential();
        if (!_pCredential) return E_OUTOFMEMORY;

        HRESULT hr = _pCredential->Initialize(_cpus);
        if (FAILED(hr))
        {
            _pCredential->Release();
            _pCredential = nullptr;
            return hr;
        }
    }

    return _pCredential->QueryInterface(IID_PPV_ARGS(ppcpc));
}
