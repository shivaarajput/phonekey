using System;

namespace PhoneKey.Core.Proximity
{
    /// <summary>
    /// 1D Kalman filter tailored for filtering noisy Bluetooth Low Energy RSSI readings.
    /// Provides smooth tracking while responding quickly to genuine position changes.
    /// </summary>
    public sealed class RssiKalmanFilter
    {
        private double _processNoise; // Q: Process variance
        private double _measurementNoise; // R: Measurement variance
        private double _estimatedRssi; // Current state estimate
        private double _errorCovariance; // Current estimation error covariance
        private bool _isInitialized;

        public RssiKalmanFilter(double processNoise = 0.125, double measurementNoise = 4.0)
        {
            _processNoise = processNoise;
            _measurementNoise = measurementNoise;
            _errorCovariance = 1.0;
            _isInitialized = false;
        }

        public double Update(double measurementRssi)
        {
            if (!_isInitialized)
            {
                _estimatedRssi = measurementRssi;
                _errorCovariance = _measurementNoise;
                _isInitialized = true;
                return _estimatedRssi;
            }

            // Prediction update
            _errorCovariance += _processNoise;

            // Measurement update
            double kalmanGain = _errorCovariance / (_errorCovariance + _measurementNoise);
            _estimatedRssi += kalmanGain * (measurementRssi - _estimatedRssi);
            _errorCovariance *= (1.0 - kalmanGain);

            return _estimatedRssi;
        }

        public void Reset()
        {
            _isInitialized = false;
            _errorCovariance = 1.0;
        }

        public double CurrentEstimate => _estimatedRssi;
        public bool IsInitialized => _isInitialized;
    }
}
