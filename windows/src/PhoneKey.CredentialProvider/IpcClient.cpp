#include "IpcClient.h"

static const WCHAR g_szPipeName[] = L"\\\\.\\pipe\\PhoneKeyAuthPipe";

static HRESULT TransactNamedPipe(
    _In_ PCSTR pszRequestJson, 
    _Out_writes_bytes_(cbResponseMax) PSTR pszResponseBuffer, 
    _In_ DWORD cbResponseMax)
{
    HANDLE hPipe = CreateFileW(
        g_szPipeName,
        GENERIC_READ | GENERIC_WRITE,
        0,
        nullptr,
        OPEN_EXISTING,
        0,
        nullptr);

    if (hPipe == INVALID_HANDLE_VALUE)
    {
        return HRESULT_FROM_WIN32(GetLastError());
    }

    DWORD cbWritten = 0;
    DWORD cbRequest = (DWORD)strlen(pszRequestJson);
    BOOL bSuccess = WriteFile(hPipe, pszRequestJson, cbRequest, &cbWritten, nullptr);
    if (!bSuccess)
    {
        CloseHandle(hPipe);
        return HRESULT_FROM_WIN32(GetLastError());
    }

    // Write newline delimiter
    CHAR szNewline[] = "\n";
    WriteFile(hPipe, szNewline, 1, &cbWritten, nullptr);

    DWORD cbRead = 0;
    ZeroMemory(pszResponseBuffer, cbResponseMax);
    bSuccess = ReadFile(hPipe, pszResponseBuffer, cbResponseMax - 1, &cbRead, nullptr);
    CloseHandle(hPipe);

    if (!bSuccess && GetLastError() != ERROR_MORE_DATA)
    {
        return HRESULT_FROM_WIN32(GetLastError());
    }

    return S_OK;
}

// Lightweight JSON field extraction helpers to avoid heavy third-party dependencies in LogonUI
static BOOL ExtractJsonBool(PCSTR pszJson, PCSTR pszKey, BOOL* pbValue)
{
    CHAR szSearch[64];
    StringCchPrintfA(szSearch, ARRAYSIZE(szSearch), "\"%s\":", pszKey);
    PCSTR p = strstr(pszJson, szSearch);
    if (!p) return FALSE;
    p += strlen(szSearch);
    while (*p == ' ') p++;
    if (strncmp(p, "true", 4) == 0) { *pbValue = TRUE; return TRUE; }
    if (strncmp(p, "false", 5) == 0) { *pbValue = FALSE; return TRUE; }
    return FALSE;
}

static BOOL ExtractJsonString(PCSTR pszJson, PCSTR pszKey, PWSTR pszOut, DWORD cchOutMax)
{
    CHAR szSearch[64];
    StringCchPrintfA(szSearch, ARRAYSIZE(szSearch), "\"%s\":\"", pszKey);
    PCSTR p = strstr(pszJson, szSearch);
    if (!p) return FALSE;
    p += strlen(szSearch);
    PCSTR pEnd = strchr(p, '\"');
    if (!pEnd) return FALSE;

    int len = (int)(pEnd - p);
    MultiByteToWideChar(CP_UTF8, 0, p, len, pszOut, cchOutMax - 1);
    pszOut[min(len, (int)cchOutMax - 1)] = L'\0';
    return TRUE;
}

HRESULT CIpcClient::QueryStatus(_Out_ PhoneKeyIpcStatus* pStatus)
{
    if (!pStatus) return E_INVALIDARG;
    ZeroMemory(pStatus, sizeof(PhoneKeyIpcStatus));

    CHAR szResp[2048];
    HRESULT hr = TransactNamedPipe("{\"Command\":1}", szResp, sizeof(szResp));
    if (FAILED(hr))
    {
        pStatus->bServiceRunning = FALSE;
        StringCchCopyW(pStatus->szStatusMessage, ARRAYSIZE(pStatus->szStatusMessage), 
            L"PhoneKey Service is not responding.");
        return hr;
    }

    pStatus->bServiceRunning = TRUE;
    ExtractJsonBool(szResp, "IsPhoneConnected", &pStatus->bPhoneConnected);
    ExtractJsonBool(szResp, "IsAuthenticated", &pStatus->bAuthenticated);
    ExtractJsonBool(szResp, "IsInProximity", &pStatus->bInProximity);
    ExtractJsonString(szResp, "ConnectedPhoneName", pStatus->szPhoneName, ARRAYSIZE(pStatus->szPhoneName));

    if (pStatus->bInProximity && pStatus->bAuthenticated)
    {
        StringCchPrintfW(pStatus->szStatusMessage, ARRAYSIZE(pStatus->szStatusMessage),
            L"PhoneKey: %s verified in proximity. Press Enter or click to unlock.", pStatus->szPhoneName);
    }
    else if (pStatus->bPhoneConnected)
    {
        StringCchPrintfW(pStatus->szStatusMessage, ARRAYSIZE(pStatus->szStatusMessage),
            L"PhoneKey: %s connected. Verifying cryptographic proximity...", pStatus->szPhoneName);
    }
    else
    {
        StringCchCopyW(pStatus->szStatusMessage, ARRAYSIZE(pStatus->szStatusMessage),
            L"PhoneKey: Bring your paired phone near PC to unlock.");
    }

    return S_OK;
}

HRESULT CIpcClient::RequestLogonCredentials(_Out_ PhoneKeyCredentials* pCredentials)
{
    if (!pCredentials) return E_INVALIDARG;
    ZeroMemory(pCredentials, sizeof(PhoneKeyCredentials));

    CHAR szResp[4096];
    HRESULT hr = TransactNamedPipe("{\"Command\":2}", szResp, sizeof(szResp));
    if (FAILED(hr)) return hr;

    BOOL bSuccess = FALSE;
    if (!ExtractJsonBool(szResp, "Success", &bSuccess) || !bSuccess)
    {
        return E_ACCESSDENIED;
    }

    ExtractJsonString(szResp, "Domain", pCredentials->szDomain, ARRAYSIZE(pCredentials->szDomain));
    ExtractJsonString(szResp, "Username", pCredentials->szUsername, ARRAYSIZE(pCredentials->szUsername));
    ExtractJsonString(szResp, "Password", pCredentials->szPassword, ARRAYSIZE(pCredentials->szPassword));

    return S_OK;
}
