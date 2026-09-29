#pragma once
#include "common.h"

struct PhoneKeyIpcStatus
{
    BOOL bServiceRunning;
    BOOL bPhoneConnected;
    BOOL bAuthenticated;
    BOOL bInProximity;
    WCHAR szPhoneName[64];
    WCHAR szStatusMessage[128];
};

struct PhoneKeyCredentials
{
    WCHAR szDomain[128];
    WCHAR szUsername[128];
    WCHAR szPassword[256];
};

class CIpcClient
{
public:
    static HRESULT QueryStatus(_Out_ PhoneKeyIpcStatus* pStatus);
    static HRESULT RequestLogonCredentials(_Out_ PhoneKeyCredentials* pCredentials);
};
