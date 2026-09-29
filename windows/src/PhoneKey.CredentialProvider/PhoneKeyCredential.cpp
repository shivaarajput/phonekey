#include "PhoneKeyCredential.h"
#include "helpers.h"

CPhoneKeyCredential::CPhoneKeyCredential() : 
    _cRef(1),
    _cpus(CPUS_LOGON),
    _pEvents(nullptr)
{
    ZeroMemory(&_currentStatus, sizeof(_currentStatus));
    DllAddRef();
}

CPhoneKeyCredential::~CPhoneKeyCredential()
{
    if (_pEvents)
    {
        _pEvents->Release();
        _pEvents = nullptr;
    }
    DllRelease();
}

HRESULT CPhoneKeyCredential::Initialize(CREDENTIAL_PROVIDER_USAGE_SCENARIO cpus)
{
    _cpus = cpus;
    UpdateStatusFromService();
    return S_OK;
}

void CPhoneKeyCredential::UpdateStatusFromService()
{
    CIpcClient::QueryStatus(&_currentStatus);
}

// IUnknown
IFACEMETHODIMP CPhoneKeyCredential::QueryInterface(REFIID riid, void** ppv)
{
    static const QITAB qit[] =
    {
        QITABENT(CPhoneKeyCredential, ICredentialProviderCredential),
        QITABENT(CPhoneKeyCredential, ICredentialProviderCredential2),
        { 0 },
    };
    return QISearch(this, qit, riid, ppv);
}

IFACEMETHODIMP_(ULONG) CPhoneKeyCredential::AddRef()
{
    return InterlockedIncrement(&_cRef);
}

IFACEMETHODIMP_(ULONG) CPhoneKeyCredential::Release()
{
    LONG cRef = InterlockedDecrement(&_cRef);
    if (cRef == 0)
    {
        delete this;
    }
    return cRef;
}

// ICredentialProviderCredential
IFACEMETHODIMP CPhoneKeyCredential::Advise(ICredentialProviderCredentialEvents* pEvents)
{
    if (_pEvents)
    {
        _pEvents->Release();
    }
    _pEvents = pEvents;
    if (_pEvents)
    {
        _pEvents->AddRef();
    }
    return S_OK;
}

IFACEMETHODIMP CPhoneKeyCredential::UnAdvise()
{
    if (_pEvents)
    {
        _pEvents->Release();
        _pEvents = nullptr;
    }
    return S_OK;
}

IFACEMETHODIMP CPhoneKeyCredential::SetSelected(BOOL* pbAutoLogon)
{
    if (pbAutoLogon)
    {
        UpdateStatusFromService();
        // If phone is authenticated and in proximity, allow auto-submitting
        *pbAutoLogon = (_currentStatus.bInProximity && _currentStatus.bAuthenticated);
    }
    return S_OK;
}

IFACEMETHODIMP CPhoneKeyCredential::SetDeselected()
{
    return S_OK;
}

IFACEMETHODIMP CPhoneKeyCredential::GetFieldState(
    DWORD dwFieldID, 
    CREDENTIAL_PROVIDER_FIELD_STATE* pcpfs, 
    CREDENTIAL_PROVIDER_FIELD_INTERACTIVE_STATE* pcpfis)
{
    if (!pcpfs || !pcpfis) return E_INVALIDARG;

    switch (dwFieldID)
    {
    case PKFI_TILEIMAGE:
        *pcpfs = CPFS_DISPLAY_IN_BOTH;
        *pcpfis = CPFIS_NONE;
        break;
    case PKFI_LABEL:
        *pcpfs = CPFS_DISPLAY_IN_BOTH;
        *pcpfis = CPFIS_NONE;
        break;
    case PKFI_SUBMIT_BUTTON:
        *pcpfs = CPFS_DISPLAY_IN_SELECTED_TILE;
        *pcpfis = CPFIS_FOCUSED;
        break;
    case PKFI_STATUS_TEXT:
        *pcpfs = CPFS_DISPLAY_IN_SELECTED_TILE;
        *pcpfis = CPFIS_NONE;
        break;
    default:
        *pcpfs = CPFS_HIDDEN;
        *pcpfis = CPFIS_NONE;
        return E_INVALIDARG;
    }

    return S_OK;
}

IFACEMETHODIMP CPhoneKeyCredential::GetStringValue(DWORD dwFieldID, PWSTR* ppsz)
{
    if (!ppsz) return E_INVALIDARG;
    *ppsz = nullptr;

    UpdateStatusFromService();

    switch (dwFieldID)
    {
    case PKFI_LABEL:
        return SHStrDupW(L"PhoneKey", ppsz);

    case PKFI_STATUS_TEXT:
        return SHStrDupW(_currentStatus.szStatusMessage, ppsz);

    default:
        return E_INVALIDARG;
    }
}

IFACEMETHODIMP CPhoneKeyCredential::GetBitmapValue(DWORD dwFieldID, HBITMAP* phbmp)
{
    if (!phbmp) return E_INVALIDARG;
    *phbmp = nullptr;

    if (dwFieldID == PKFI_TILEIMAGE)
    {
        // Load default credential bitmap or standard icon
        *phbmp = (HBITMAP)LoadImageW(nullptr, IDI_SHIELD, IMAGE_ICON, 48, 48, LR_SHARED);
        return S_OK;
    }

    return E_INVALIDARG;
}

IFACEMETHODIMP CPhoneKeyCredential::GetCheckboxValue(DWORD, BOOL*, PWSTR*) { return E_NOTIMPL; }
IFACEMETHODIMP CPhoneKeyCredential::SetCheckboxValue(DWORD, BOOL) { return E_NOTIMPL; }

IFACEMETHODIMP CPhoneKeyCredential::GetSubmitButtonValue(DWORD dwFieldID, DWORD* pdwAdjacentTo)
{
    if (!pdwAdjacentTo) return E_INVALIDARG;
    if (dwFieldID == PKFI_SUBMIT_BUTTON)
    {
        *pdwAdjacentTo = PKFI_STATUS_TEXT;
        return S_OK;
    }
    return E_INVALIDARG;
}

IFACEMETHODIMP CPhoneKeyCredential::GetComboBoxValueCount(DWORD, DWORD*, DWORD*) { return E_NOTIMPL; }
IFACEMETHODIMP CPhoneKeyCredential::GetComboBoxValueAt(DWORD, DWORD, PWSTR*) { return E_NOTIMPL; }
IFACEMETHODIMP CPhoneKeyCredential::SetComboBoxSelectedValue(DWORD, DWORD) { return E_NOTIMPL; }
IFACEMETHODIMP CPhoneKeyCredential::SetStringValue(DWORD, PCWSTR) { return E_NOTIMPL; }

IFACEMETHODIMP CPhoneKeyCredential::CommandLinkClicked(DWORD) { return E_NOTIMPL; }

IFACEMETHODIMP CPhoneKeyCredential::GetSerialization(
    CREDENTIAL_PROVIDER_GET_SERIALIZATION_RESPONSE* pcpgsr,
    CREDENTIAL_PROVIDER_CREDENTIAL_SERIALIZATION* pcpcs,
    PWSTR* ppszOptionalStatusText,
    CREDENTIAL_PROVIDER_STATUS_ICON* pcpsiOptionalStatusIcon)
{
    if (!pcpgsr || !pcpcs) return E_INVALIDARG;

    *pcpgsr = CPGSR_NO_CREDENTIAL_NOT_FINISHED;
    ZeroMemory(pcpcs, sizeof(*pcpcs));

    UpdateStatusFromService();

    if (!_currentStatus.bInProximity || !_currentStatus.bAuthenticated)
    {
        if (ppszOptionalStatusText)
        {
            SHStrDupW(L"PhoneKey: Paired phone is not in proximity or not authenticated.", ppszOptionalStatusText);
        }
        if (pcpsiOptionalStatusIcon)
        {
            *pcpsiOptionalStatusIcon = CPSI_WARNING;
        }
        return S_OK;
    }

    // Request unsealed credentials from the PhoneKey Service
    PhoneKeyCredentials creds;
    HRESULT hr = CIpcClient::RequestLogonCredentials(&creds);
    if (FAILED(hr))
    {
        if (ppszOptionalStatusText)
        {
            SHStrDupW(L"PhoneKey: Credential release denied by service.", ppszOptionalStatusText);
        }
        return S_OK;
    }

    BYTE* pLogonBuffer = nullptr;
    DWORD cbLogonBuffer = 0;
    hr = KerbInteractiveLogonPack(
        creds.szDomain, 
        creds.szUsername, 
        creds.szPassword, 
        &pLogonBuffer, 
        &cbLogonBuffer);

    // Immediately sanitize plaintext password from memory
    SecureZeroMemory(&creds, sizeof(creds));

    if (FAILED(hr))
    {
        return hr;
    }

    ULONG ulAuthPackage = 0;
    hr = RetrieveNegotiateAuthPackage(&ulAuthPackage);
    if (FAILED(hr))
    {
        CoTaskMemFree(pLogonBuffer);
        return hr;
    }

    pcpcs->ulAuthenticationPackage = ulAuthPackage;
    pcpcs->cbSerialization = cbLogonBuffer;
    pcpcs->rgbSerialization = pLogonBuffer;
    pcpcs->clsidCredentialProvider = CLSID_PhoneKeyCredentialProvider;

    *pcpgsr = CPGSR_RETURN_CREDENTIAL_FINISHED;
    return S_OK;
}

IFACEMETHODIMP CPhoneKeyCredential::ReportResult(
    NTSTATUS,
    NTSTATUS,
    PWSTR*,
    CREDENTIAL_PROVIDER_STATUS_ICON*)
{
    return S_OK;
}

IFACEMETHODIMP CPhoneKeyCredential::GetUserSid(PWSTR* ppszSid)
{
    if (!ppszSid) return E_INVALIDARG;
    *ppszSid = nullptr;
    return E_NOTIMPL; // System will discover SID from LSA logon response
}
