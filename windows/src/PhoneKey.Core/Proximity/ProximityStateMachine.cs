using System;

namespace PhoneKey.Core.Proximity
{
    public sealed class ProximityConfig
    {
        /// <summary>
        /// RSSI at or above which the phone is considered physically nearby (eligible for unlock).
        /// Default: -65 dBm (within approx 1-2 meters unobstructed).
        /// </summary>
        public int UnlockThresholdRssi { get; set; } = -65;

        /// <summary>
        /// RSSI at or below which the phone is considered leaving/distant.
        /// Must be lower than UnlockThresholdRssi to provide hysteresis.
        /// Default: -85 dBm.
        /// </summary>
        public int LockThresholdRssi { get; set; } = -85;

        /// <summary>
        /// Duration of absence or weak signal before triggering an automatic Windows lock.
        /// Default: 15 seconds.
        /// </summary>
        public int GracePeriodSeconds { get; set; } = 15;

        /// <summary>
        /// Master switch for proximity auto-locking.
        /// </summary>
        public bool AutoLockEnabled { get; set; } = true;

        /// <summary>
        /// Require biometric / user presence on phone before unlocking.
        /// </summary>
        public bool RequireBiometrics { get; set; } = false;
    }

    public enum ProximityState
    {
        Disconnected,
        Connecting,
        Authenticating,
        AuthenticatedOutOfRange,
        InProximity,
        GracePeriod,
        LockTriggered
    }

    public sealed class ProximityStateMachine
    {
        private readonly ProximityConfig _config;
        private readonly RssiKalmanFilter _filter = new();
        private ProximityState _currentState = ProximityState.Disconnected;
        private DateTimeOffset _lastSeenUtc = DateTimeOffset.MinValue;
        private DateTimeOffset _gracePeriodStartedUtc = DateTimeOffset.MinValue;

        public event Action<ProximityState>? StateChanged;
        public event Action? LockTriggered;

        public ProximityStateMachine(ProximityConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public ProximityState CurrentState => _currentState;
        public double FilteredRssi => _filter.CurrentEstimate;
        public TimeSpan TimeSinceLastSeen => DateTimeOffset.UtcNow - _lastSeenUtc;
        public TimeSpan GracePeriodRemaining
        {
            get
            {
                if (_currentState != ProximityState.GracePeriod) return TimeSpan.Zero;
                var elapsed = DateTimeOffset.UtcNow - _gracePeriodStartedUtc;
                var total = TimeSpan.FromSeconds(_config.GracePeriodSeconds);
                return elapsed < total ? total - elapsed : TimeSpan.Zero;
            }
        }

        public void NotifyConnected()
        {
            _filter.Reset();
            TransitionTo(ProximityState.Authenticating);
        }

        public void NotifyAuthenticated()
        {
            _lastSeenUtc = DateTimeOffset.UtcNow;
            TransitionTo(ProximityState.AuthenticatedOutOfRange);
        }

        public void ProcessRssi(double rawRssi)
        {
            _lastSeenUtc = DateTimeOffset.UtcNow;
            double smooth = _filter.Update(rawRssi);

            if (_currentState == ProximityState.InProximity)
            {
                if (smooth <= _config.LockThresholdRssi)
                {
                    StartGracePeriod();
                }
            }
            else if (_currentState == ProximityState.GracePeriod)
            {
                if (smooth >= _config.UnlockThresholdRssi)
                {
                    // Returned before grace period expired
                    TransitionTo(ProximityState.InProximity);
                }
            }
            else if (_currentState == ProximityState.AuthenticatedOutOfRange)
            {
                if (smooth >= _config.UnlockThresholdRssi)
                {
                    TransitionTo(ProximityState.InProximity);
                }
            }
        }

        public void ProcessTick()
        {
            var now = DateTimeOffset.UtcNow;

            if (_currentState == ProximityState.InProximity)
            {
                // If packets have stopped arriving for > 3.5s (missed 2-3 heartbeats)
                if ((now - _lastSeenUtc).TotalSeconds > 3.5)
                {
                    StartGracePeriod();
                }
            }
            else if (_currentState == ProximityState.GracePeriod)
            {
                if ((now - _gracePeriodStartedUtc).TotalSeconds >= _config.GracePeriodSeconds)
                {
                    if (_config.AutoLockEnabled)
                    {
                        TransitionTo(ProximityState.LockTriggered);
                        LockTriggered?.Invoke();
                    }
                    else
                    {
                        TransitionTo(ProximityState.Disconnected);
                    }
                }
            }
            else if (_currentState == ProximityState.Authenticating)
            {
                if ((now - _lastSeenUtc).TotalSeconds > 10.0)
                {
                    TransitionTo(ProximityState.Disconnected);
                }
            }
        }

        public void NotifyDisconnected()
        {
            if (_currentState == ProximityState.InProximity)
            {
                StartGracePeriod();
            }
            else if (_currentState != ProximityState.GracePeriod && _currentState != ProximityState.LockTriggered)
            {
                TransitionTo(ProximityState.Disconnected);
            }
        }

        private void StartGracePeriod()
        {
            _gracePeriodStartedUtc = DateTimeOffset.UtcNow;
            TransitionTo(ProximityState.GracePeriod);
        }

        private void TransitionTo(ProximityState newState)
        {
            if (_currentState == newState) return;
            _currentState = newState;
            StateChanged?.Invoke(_currentState);
        }
    }
}
