// Every shape's own Parse and TryParse, so a value typed as one shape reads only a literal of that
// shape. They all go through RangeFormat.ParseVariant, which the shapes' TypeConverter uses too (#116).

using System.Diagnostics.CodeAnalysis;
using NodaTime;
using CodoMetis.ValueRanges.Internals;

namespace CodoMetis.ValueRanges;

public abstract partial record LocalDateRange
{
    public sealed partial record EmptyRange
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="EmptyRange"/>, using the same invariant
        /// grammar as <see cref="LocalDateRange.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new EmptyRange Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<LocalDateRange, LocalDate, EmptyRange>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="EmptyRange"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out EmptyRange? result)
            => RangeFormat.TryParseVariant<LocalDateRange, LocalDate, EmptyRange>(s, out result);
    }

    public sealed partial record Finite
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="Finite"/>, using the same invariant
        /// grammar as <see cref="LocalDateRange.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new Finite Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<LocalDateRange, LocalDate, Finite>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="Finite"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out Finite? result)
            => RangeFormat.TryParseVariant<LocalDateRange, LocalDate, Finite>(s, out result);
    }

    public sealed partial record UnboundedStart
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="UnboundedStart"/>, using the same invariant
        /// grammar as <see cref="LocalDateRange.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new UnboundedStart Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<LocalDateRange, LocalDate, UnboundedStart>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="UnboundedStart"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out UnboundedStart? result)
            => RangeFormat.TryParseVariant<LocalDateRange, LocalDate, UnboundedStart>(s, out result);
    }

    public sealed partial record UnboundedEnd
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="UnboundedEnd"/>, using the same invariant
        /// grammar as <see cref="LocalDateRange.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new UnboundedEnd Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<LocalDateRange, LocalDate, UnboundedEnd>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="UnboundedEnd"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out UnboundedEnd? result)
            => RangeFormat.TryParseVariant<LocalDateRange, LocalDate, UnboundedEnd>(s, out result);
    }

    public sealed partial record Infinity
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="Infinity"/>, using the same invariant
        /// grammar as <see cref="LocalDateRange.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new Infinity Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<LocalDateRange, LocalDate, Infinity>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="Infinity"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out Infinity? result)
            => RangeFormat.TryParseVariant<LocalDateRange, LocalDate, Infinity>(s, out result);
    }
}

public abstract partial record LocalDateTimeRange
{
    public sealed partial record EmptyRange
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="EmptyRange"/>, using the same invariant
        /// grammar as <see cref="LocalDateTimeRange.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new EmptyRange Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<LocalDateTimeRange, LocalDateTime, EmptyRange>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="EmptyRange"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out EmptyRange? result)
            => RangeFormat.TryParseVariant<LocalDateTimeRange, LocalDateTime, EmptyRange>(s, out result);
    }

    public sealed partial record Finite
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="Finite"/>, using the same invariant
        /// grammar as <see cref="LocalDateTimeRange.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new Finite Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<LocalDateTimeRange, LocalDateTime, Finite>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="Finite"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out Finite? result)
            => RangeFormat.TryParseVariant<LocalDateTimeRange, LocalDateTime, Finite>(s, out result);
    }

    public sealed partial record UnboundedStart
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="UnboundedStart"/>, using the same invariant
        /// grammar as <see cref="LocalDateTimeRange.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new UnboundedStart Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<LocalDateTimeRange, LocalDateTime, UnboundedStart>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="UnboundedStart"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out UnboundedStart? result)
            => RangeFormat.TryParseVariant<LocalDateTimeRange, LocalDateTime, UnboundedStart>(s, out result);
    }

    public sealed partial record UnboundedEnd
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="UnboundedEnd"/>, using the same invariant
        /// grammar as <see cref="LocalDateTimeRange.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new UnboundedEnd Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<LocalDateTimeRange, LocalDateTime, UnboundedEnd>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="UnboundedEnd"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out UnboundedEnd? result)
            => RangeFormat.TryParseVariant<LocalDateTimeRange, LocalDateTime, UnboundedEnd>(s, out result);
    }

    public sealed partial record Infinity
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="Infinity"/>, using the same invariant
        /// grammar as <see cref="LocalDateTimeRange.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new Infinity Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<LocalDateTimeRange, LocalDateTime, Infinity>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="Infinity"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out Infinity? result)
            => RangeFormat.TryParseVariant<LocalDateTimeRange, LocalDateTime, Infinity>(s, out result);
    }
}

public abstract partial record InstantRange
{
    public sealed partial record EmptyRange
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="EmptyRange"/>, using the same invariant
        /// grammar as <see cref="InstantRange.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new EmptyRange Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<InstantRange, Instant, EmptyRange>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="EmptyRange"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out EmptyRange? result)
            => RangeFormat.TryParseVariant<InstantRange, Instant, EmptyRange>(s, out result);
    }

    public sealed partial record Finite
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="Finite"/>, using the same invariant
        /// grammar as <see cref="InstantRange.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new Finite Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<InstantRange, Instant, Finite>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="Finite"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out Finite? result)
            => RangeFormat.TryParseVariant<InstantRange, Instant, Finite>(s, out result);
    }

    public sealed partial record UnboundedStart
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="UnboundedStart"/>, using the same invariant
        /// grammar as <see cref="InstantRange.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new UnboundedStart Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<InstantRange, Instant, UnboundedStart>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="UnboundedStart"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out UnboundedStart? result)
            => RangeFormat.TryParseVariant<InstantRange, Instant, UnboundedStart>(s, out result);
    }

    public sealed partial record UnboundedEnd
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="UnboundedEnd"/>, using the same invariant
        /// grammar as <see cref="InstantRange.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new UnboundedEnd Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<InstantRange, Instant, UnboundedEnd>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="UnboundedEnd"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out UnboundedEnd? result)
            => RangeFormat.TryParseVariant<InstantRange, Instant, UnboundedEnd>(s, out result);
    }

    public sealed partial record Infinity
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="Infinity"/>, using the same invariant
        /// grammar as <see cref="InstantRange.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new Infinity Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<InstantRange, Instant, Infinity>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="Infinity"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out Infinity? result)
            => RangeFormat.TryParseVariant<InstantRange, Instant, Infinity>(s, out result);
    }
}

public abstract partial record YearMonthRange
{
    public sealed partial record EmptyRange
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="EmptyRange"/>, using the same invariant
        /// grammar as <see cref="YearMonthRange.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new EmptyRange Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<YearMonthRange, YearMonth, EmptyRange>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="EmptyRange"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out EmptyRange? result)
            => RangeFormat.TryParseVariant<YearMonthRange, YearMonth, EmptyRange>(s, out result);
    }

    public sealed partial record Finite
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="Finite"/>, using the same invariant
        /// grammar as <see cref="YearMonthRange.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new Finite Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<YearMonthRange, YearMonth, Finite>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="Finite"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out Finite? result)
            => RangeFormat.TryParseVariant<YearMonthRange, YearMonth, Finite>(s, out result);
    }

    public sealed partial record UnboundedStart
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="UnboundedStart"/>, using the same invariant
        /// grammar as <see cref="YearMonthRange.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new UnboundedStart Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<YearMonthRange, YearMonth, UnboundedStart>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="UnboundedStart"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out UnboundedStart? result)
            => RangeFormat.TryParseVariant<YearMonthRange, YearMonth, UnboundedStart>(s, out result);
    }

    public sealed partial record UnboundedEnd
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="UnboundedEnd"/>, using the same invariant
        /// grammar as <see cref="YearMonthRange.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new UnboundedEnd Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<YearMonthRange, YearMonth, UnboundedEnd>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="UnboundedEnd"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out UnboundedEnd? result)
            => RangeFormat.TryParseVariant<YearMonthRange, YearMonth, UnboundedEnd>(s, out result);
    }

    public sealed partial record Infinity
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="Infinity"/>, using the same invariant
        /// grammar as <see cref="YearMonthRange.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new Infinity Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<YearMonthRange, YearMonth, Infinity>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="Infinity"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out Infinity? result)
            => RangeFormat.TryParseVariant<YearMonthRange, YearMonth, Infinity>(s, out result);
    }
}
