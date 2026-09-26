using System;
using Xunit;

namespace GrandStrategy.Simulation.Tests
{
    public class GameDateTests
    {
        [Theory]
        [InlineData(2024, true)]
        [InlineData(2026, false)]
        [InlineData(2100, false)]
        [InlineData(2000, true)]
        public void LeapYears(int year, bool leap) => Assert.Equal(leap, GameDate.IsLeapYear(year));

        [Fact]
        public void NextDayRollsOverMonthsAndYears()
        {
            Assert.Equal(new GameDate(2026, 2, 1), new GameDate(2026, 1, 31).NextDay());
            Assert.Equal(new GameDate(2026, 3, 1), new GameDate(2026, 2, 28).NextDay());
            Assert.Equal(new GameDate(2028, 2, 29), new GameDate(2028, 2, 28).NextDay());
            Assert.Equal(new GameDate(2027, 1, 1), new GameDate(2026, 12, 31).NextDay());
        }

        [Fact]
        public void AddDaysCoversAWholeYear()
        {
            Assert.Equal(new GameDate(2027, 1, 1), new GameDate(2026, 1, 1).AddDays(365));
            Assert.Equal(new GameDate(2029, 1, 1), new GameDate(2028, 1, 1).AddDays(366));
        }

        [Fact]
        public void RejectsInvalidDates()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new GameDate(2026, 2, 29));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GameDate(2026, 13, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GameDate(2026, 1, 1).AddDays(-1));
        }

        [Fact]
        public void FormatsForTheUi() => Assert.Equal("1 January 2026", new GameDate(2026, 1, 1).ToString());

        [Fact]
        public void Compares()
        {
            Assert.True(new GameDate(2026, 5, 1) > new GameDate(2026, 4, 30));
            Assert.True(new GameDate(2025, 12, 31) < new GameDate(2026, 1, 1));
        }
    }
}
