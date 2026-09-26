using System;

namespace GrandStrategy.Simulation
{
    /// <summary>A calendar date in the game world (Gregorian calendar).</summary>
    public readonly struct GameDate : IEquatable<GameDate>, IComparable<GameDate>
    {
        static readonly int[] DaysPerMonth = { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };

        static readonly string[] MonthNames =
        {
            "January", "February", "March", "April", "May", "June",
            "July", "August", "September", "October", "November", "December",
        };

        public readonly int Year;
        public readonly int Month;
        public readonly int Day;

        public GameDate(int year, int month, int day)
        {
            if (month < 1 || month > 12)
                throw new ArgumentOutOfRangeException(nameof(month), month, "Month must be 1-12.");
            if (day < 1 || day > DaysInMonth(year, month))
                throw new ArgumentOutOfRangeException(nameof(day), day, $"Day must be 1-{DaysInMonth(year, month)}.");
            Year = year;
            Month = month;
            Day = day;
        }

        public string MonthName => MonthNames[Month - 1];

        public static bool IsLeapYear(int year) => (year % 4 == 0 && year % 100 != 0) || year % 400 == 0;

        public static int DaysInMonth(int year, int month) =>
            month == 2 && IsLeapYear(year) ? 29 : DaysPerMonth[month - 1];

        public GameDate NextDay()
        {
            if (Day < DaysInMonth(Year, Month))
                return new GameDate(Year, Month, Day + 1);
            if (Month < 12)
                return new GameDate(Year, Month + 1, 1);
            return new GameDate(Year + 1, 1, 1);
        }

        public GameDate AddDays(int days)
        {
            if (days < 0)
                throw new ArgumentOutOfRangeException(nameof(days), days, "Only forward time is supported.");
            var date = this;
            for (int i = 0; i < days; i++)
                date = date.NextDay();
            return date;
        }

        public int CompareTo(GameDate other)
        {
            if (Year != other.Year) return Year.CompareTo(other.Year);
            if (Month != other.Month) return Month.CompareTo(other.Month);
            return Day.CompareTo(other.Day);
        }

        public bool Equals(GameDate other) => Year == other.Year && Month == other.Month && Day == other.Day;
        public override bool Equals(object obj) => obj is GameDate other && Equals(other);
        public override int GetHashCode() => (Year * 12 + Month) * 31 + Day;

        public static bool operator ==(GameDate a, GameDate b) => a.Equals(b);
        public static bool operator !=(GameDate a, GameDate b) => !a.Equals(b);
        public static bool operator <(GameDate a, GameDate b) => a.CompareTo(b) < 0;
        public static bool operator >(GameDate a, GameDate b) => a.CompareTo(b) > 0;
        public static bool operator <=(GameDate a, GameDate b) => a.CompareTo(b) <= 0;
        public static bool operator >=(GameDate a, GameDate b) => a.CompareTo(b) >= 0;

        public override string ToString() => $"{Day} {MonthName} {Year}";
    }
}
