using System;

namespace GrandStrategy.Simulation
{
    /// <summary>
    /// Pausable real-time clock. Converts real seconds into in-game days at the selected speed.
    /// </summary>
    public sealed class GameClock
    {
        public const int MinSpeed = 1;
        public const int MaxSpeed = 5;

        /// <summary>Real seconds per in-game day, indexed by speed (index 0 unused).</summary>
        static readonly double[] SecondsPerDay = { 0, 1.0, 0.5, 0.2, 0.08, 0.02 };

        /// <summary>Stops a long frame hitch from simulating a huge batch of days at once.</summary>
        const int MaxDaysPerAdvance = 10;

        double _accumulator;

        public GameClock(GameDate start)
        {
            Date = start;
        }

        public GameDate Date { get; private set; }
        public int Speed { get; private set; } = 2;
        public bool IsPaused { get; private set; } = true;

        /// <summary>Progress towards the next day, 0..1. Useful for UI.</summary>
        public double DayProgress => IsPaused ? 0 : Math.Min(1.0, _accumulator / SecondsPerDay[Speed]);

        public event Action<GameDate> DayPassed;
        public event Action<GameDate> MonthPassed;
        public event Action<GameDate> YearPassed;

        /// <summary>Raised when the speed or pause state changes.</summary>
        public event Action StateChanged;

        /// <summary>Advances the clock by real time. Returns the number of days that passed.</summary>
        public int Advance(double realSeconds)
        {
            if (IsPaused || realSeconds <= 0)
                return 0;

            _accumulator += realSeconds;
            double perDay = SecondsPerDay[Speed];
            int days = 0;
            // Stop as soon as something pauses the clock mid-advance (an event raised on a
            // month boundary must not let further days slip by in the same frame).
            while (!IsPaused && _accumulator >= perDay && days < MaxDaysPerAdvance)
            {
                _accumulator -= perDay;
                StepDay();
                days++;
            }
            if (days == MaxDaysPerAdvance)
                _accumulator = Math.Min(_accumulator, perDay);
            return days;
        }

        /// <summary>Moves the date forward by exactly one day, firing all events.</summary>
        public void StepDay()
        {
            var previous = Date;
            Date = Date.NextDay();
            DayPassed?.Invoke(Date);
            if (Date.Month != previous.Month)
                MonthPassed?.Invoke(Date);
            if (Date.Year != previous.Year)
                YearPassed?.Invoke(Date);
        }

        public void SetSpeed(int speed)
        {
            speed = Math.Max(MinSpeed, Math.Min(MaxSpeed, speed));
            if (speed == Speed)
                return;
            Speed = speed;
            StateChanged?.Invoke();
        }

        public void SpeedUp() => SetSpeed(Speed + 1);
        public void SlowDown() => SetSpeed(Speed - 1);

        public void SetPaused(bool paused)
        {
            if (paused == IsPaused)
                return;
            IsPaused = paused;
            if (paused)
                _accumulator = 0;
            StateChanged?.Invoke();
        }

        public void TogglePause() => SetPaused(!IsPaused);
    }
}
