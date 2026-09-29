#pragma once
#include "common.h"

HRESULT KerbInteractiveLogonPack(
    _In_ PCWSTR pszDomain,
    _In_ PCWSTR pszUsername,
    _In_ PCWSTR pszPassword,
    _Outptr_result_bytebuffer_(*pcbLogonBuffer) BYTE** ppbLogonBuffer,
    _Out_ DWORD* pcbLogonBuffer
);

HRESULT RetrieveNegotiateAuthPackage(_Out_ ULONG* pulAuthPackage);
