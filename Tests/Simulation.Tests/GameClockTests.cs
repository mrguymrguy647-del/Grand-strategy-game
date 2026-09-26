using System.Collections.Generic;
using Xunit;

namespace GrandStrategy.Simulation.Tests
{
    public class GameClockTests
    {
        [Fact]
        public void StartsPausedAndDoesNotAdvance()
        {
            var clock = new GameClock(new GameDate(2026, 1, 1));
            Assert.True(clock.IsPaused);
            Assert.Equal(0, clock.Advance(100));
            Assert.Equal(new GameDate(2026, 1, 1), clock.Date);
        }

        [Fact]
        public void SpeedOneIsOneDayPerSecond()
        {
            var clock = new GameClock(new GameDate(2026, 1, 1));
            clock.SetSpeed(1);
            clock.SetPaused(false);
            Assert.Equal(0, clock.Advance(0.6));
            Assert.Equal(1, clock.Advance(0.6));
            Assert.Equal(new GameDate(2026, 1, 2), clock.Date);
        }

        [Fact]
        public void FasterSpeedsPassMoreDays()
        {
            var clock = new GameClock(new GameDate(2026, 1, 1));
            clock.SetSpeed(3);
            clock.SetPaused(false);
            Assert.Equal(5, clock.Advance(1.0));
        }

        [Fact]
        public void LongFramesAreCapped()
        {
            var clock = new GameClock(new GameDate(2026, 1, 1));
            clock.SetSpeed(5);
            clock.SetPaused(false);
            Assert.Equal(10, clock.Advance(30));
            Assert.True(clock.Advance(0) == 0);
        }

        [Fact]
        public void SpeedIsClamped()
        {
            var clock = new GameClock(new GameDate(2026, 1, 1));
            clock.SetSpeed(99);
            Assert.Equal(GameClock.MaxSpeed, clock.Speed);
            clock.SetSpeed(-3);
            Assert.Equal(GameClock.MinSpeed, clock.Speed);
        }

        [Fact]
        public void FiresMonthAndYearEvents()
        {
            var clock = new GameClock(new GameDate(2026, 12, 30));
            var log = new List<string>();
            clock.DayPassed += d => log.Add("day " + d);
            clock.MonthPassed += d => log.Add("month " + d);
            clock.YearPassed += d => log.Add("year " + d);

            clock.StepDay();
            clock.StepDay();

            Assert.Equal(new[]
            {
                "day 31 December 2026",
                "day 1 January 2027",
                "month 1 January 2027",
                "year 1 January 2027",
            }, log);
        }

        [Fact]
        public void StateChangedFiresOnlyOnRealChanges()
        {
            var clock = new GameClock(new GameDate(2026, 1, 1));
            int changes = 0;
            clock.StateChanged += () => changes++;
            clock.SetPaused(true); // already paused
            clock.TogglePause();
            clock.SpeedUp();
            clock.SetSpeed(clock.Speed);
            Assert.Equal(2, changes);
        }
    }
}
