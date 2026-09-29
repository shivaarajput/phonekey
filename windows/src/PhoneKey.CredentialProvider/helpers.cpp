#include "helpers.h"

HRESULT KerbInteractiveLogonPack(
    _In_ PCWSTR pszDomain,
    _In_ PCWSTR pszUsername,
    _In_ PCWSTR pszPassword,
    _Outptr_result_bytebuffer_(*pcbLogonBuffer) BYTE** ppbLogonBuffer,
    _Out_ DWORD* pcbLogonBuffer)
{
    if (!pszUsername || !pszPassword || !ppbLogonBuffer || !pcbLogonBuffer)
    {
        return E_INVALIDARG;
    }

    *ppbLogonBuffer = nullptr;
    *pcbLogonBuffer = 0;

    DWORD cbDomain = pszDomain ? (DWORD)(wcslen(pszDomain) * sizeof(WCHAR)) : 0;
    DWORD cbUsername = (DWORD)(wcslen(pszUsername) * sizeof(WCHAR));
    DWORD cbPassword = (DWORD)(wcslen(pszPassword) * sizeof(WCHAR));

    DWORD cbTotal = sizeof(KERB_INTERACTIVE_LOGON) + cbDomain + cbUsername + cbPassword;

    BYTE* pBuffer = (BYTE*)CoTaskMemAlloc(cbTotal);
    if (!pBuffer)
    {
        return E_OUTOFMEMORY;
    }

    ZeroMemory(pBuffer, cbTotal);

    PKERB_INTERACTIVE_LOGON pLogon = (PKERB_INTERACTIVE_LOGON)pBuffer;
    pLogon->MessageType = KerbInteractiveLogon;

    BYTE* pRunning = pBuffer + sizeof(KERB_INTERACTIVE_LOGON);

    // Copy Domain
    if (cbDomain > 0)
    {
        CopyMemory(pRunning, pszDomain, cbDomain);
        pLogon->LogonDomainName.Length = (USHORT)cbDomain;
        pLogon->LogonDomainName.MaximumLength = (USHORT)cbDomain;
        pLogon->LogonDomainName.Buffer = (PWSTR)pRunning;
        pRunning += cbDomain;
    }

    // Copy Username
    CopyMemory(pRunning, pszUsername, cbUsername);
    pLogon->UserName.Length = (USHORT)cbUsername;
    pLogon->UserName.MaximumLength = (USHORT)cbUsername;
    pLogon->UserName.Buffer = (PWSTR)pRunning;
    pRunning += cbUsername;

    // Copy Password
    CopyMemory(pRunning, pszPassword, cbPassword);
    pLogon->Password.Length = (USHORT)cbPassword;
    pLogon->Password.MaximumLength = (USHORT)cbPassword;
    pLogon->Password.Buffer = (PWSTR)pRunning;

    *ppbLogonBuffer = pBuffer;
    *pcbLogonBuffer = cbTotal;

    return S_OK;
}

HRESULT RetrieveNegotiateAuthPackage(_Out_ ULONG* pulAuthPackage)
{
    if (!pulAuthPackage) return E_INVALIDARG;
    *pulAuthPackage = 0;

    HANDLE hLsa = nullptr;
    NTSTATUS status = LsaConnectUntrusted(&hLsa);
    if (status != 0 || !hLsa)
    {
        return HRESULT_FROM_WIN32(LsaNtStatusToWinError(status));
    }

    LSA_STRING packageName;
    CHAR szPackage[] = NEGOSSP_NAME_A;
    packageName.Buffer = szPackage;
    packageName.Length = (USHORT)strlen(szPackage);
    packageName.MaximumLength = packageName.Length + 1;

    ULONG packageId = 0;
    status = LsaLookupAuthenticationPackage(hLsa, &packageName, &packageId);
    LsaDeregisterLogonProcess(hLsa);

    if (status != 0)
    {
        return HRESULT_FROM_WIN32(LsaNtStatusToWinError(status));
    }

    *pulAuthPackage = packageId;
    return S_OK;
}
