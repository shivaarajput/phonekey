#pragma once

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif

#ifndef SECURITY_WIN32
#define SECURITY_WIN32
#endif

#include <windows.h>
#include <strsafe.h>
#include <shlwapi.h>
#include <credentialprovider.h>
#include <ntsecapi.h>
#include <subauth.h>
#include <security.h>
#include <new>

#ifndef NEGOSSP_NAME_A
#define NEGOSSP_NAME_A "Negotiate"
#endif

#include "guid.h"

// Field indices for the Credential Tile UI
enum PHONEKEY_FIELD_ID
{
    PKFI_TILEIMAGE = 0,
    PKFI_LABEL = 1,
    PKFI_SUBMIT_BUTTON = 2,
    PKFI_STATUS_TEXT = 3,
    PKFI_NUM_FIELDS = 4
};

extern LONG g_cRef;

void DllAddRef();
void DllRelease();
