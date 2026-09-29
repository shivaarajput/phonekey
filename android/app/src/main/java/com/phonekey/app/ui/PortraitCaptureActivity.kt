package com.phonekey.app.ui

import com.journeyapps.barcodescanner.CaptureActivity

/**
 * Custom ZXing CaptureActivity locked strictly in portrait mode.
 * Prevents camera orientation rotation issues during QR code pairing.
 */
class PortraitCaptureActivity : CaptureActivity()
