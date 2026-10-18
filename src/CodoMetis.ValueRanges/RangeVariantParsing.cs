// Every shape's own Parse and TryParse, so a value typed as one shape reads only a literal of that
// shape. They all go through RangeFormat.ParseVariant, which the shapes' TypeConverter uses too (#116).

using System.Diagnostics.CodeAnalysis;
using CodoMetis.ValueRanges.Internals;

namespace CodoMetis.ValueRanges;

public abstract partial record Int32Range
{
    public sealed partial record EmptyRange
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="EmptyRange"/>, using the same invariant
        /// grammar as <see cref="Int32Range.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new EmptyRange Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<Int32Range, int, EmptyRange>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="EmptyRange"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out EmptyRange? result)
            => RangeFormat.TryParseVariant<Int32Range, int, EmptyRange>(s, out result);
    }

    public sealed partial record Finite
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="Finite"/>, using the same invariant
        /// grammar as <see cref="Int32Range.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new Finite Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<Int32Range, int, Finite>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="Finite"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out Finite? result)
            => RangeFormat.TryParseVariant<Int32Range, int, Finite>(s, out result);
    }

    public sealed partial record UnboundedStart
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="UnboundedStart"/>, using the same invariant
        /// grammar as <see cref="Int32Range.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new UnboundedStart Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<Int32Range, int, UnboundedStart>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="UnboundedStart"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out UnboundedStart? result)
            => RangeFormat.TryParseVariant<Int32Range, int, UnboundedStart>(s, out result);
    }

    public sealed partial record UnboundedEnd
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="UnboundedEnd"/>, using the same invariant
        /// grammar as <see cref="Int32Range.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new UnboundedEnd Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<Int32Range, int, UnboundedEnd>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="UnboundedEnd"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out UnboundedEnd? result)
            => RangeFormat.TryParseVariant<Int32Range, int, UnboundedEnd>(s, out result);
    }

    public sealed partial record Infinity
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="Infinity"/>, using the same invariant
        /// grammar as <see cref="Int32Range.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new Infinity Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<Int32Range, int, Infinity>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="Infinity"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out Infinity? result)
            => RangeFormat.TryParseVariant<Int32Range, int, Infinity>(s, out result);
    }
}

public abstract partial record Int64Range
{
    public sealed partial record EmptyRange
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="EmptyRange"/>, using the same invariant
        /// grammar as <see cref="Int64Range.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new EmptyRange Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<Int64Range, long, EmptyRange>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="EmptyRange"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out EmptyRange? result)
            => RangeFormat.TryParseVariant<Int64Range, long, EmptyRange>(s, out result);
    }

    public sealed partial record Finite
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="Finite"/>, using the same invariant
        /// grammar as <see cref="Int64Range.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new Finite Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<Int64Range, long, Finite>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="Finite"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out Finite? result)
            => RangeFormat.TryParseVariant<Int64Range, long, Finite>(s, out result);
    }

    public sealed partial record UnboundedStart
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="UnboundedStart"/>, using the same invariant
        /// grammar as <see cref="Int64Range.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new UnboundedStart Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<Int64Range, long, UnboundedStart>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="UnboundedStart"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out UnboundedStart? result)
            => RangeFormat.TryParseVariant<Int64Range, long, UnboundedStart>(s, out result);
    }

    public sealed partial record UnboundedEnd
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="UnboundedEnd"/>, using the same invariant
        /// grammar as <see cref="Int64Range.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new UnboundedEnd Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<Int64Range, long, UnboundedEnd>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="UnboundedEnd"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out UnboundedEnd? result)
            => RangeFormat.TryParseVariant<Int64Range, long, UnboundedEnd>(s, out result);
    }

    public sealed partial record Infinity
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="Infinity"/>, using the same invariant
        /// grammar as <see cref="Int64Range.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new Infinity Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<Int64Range, long, Infinity>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="Infinity"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out Infinity? result)
            => RangeFormat.TryParseVariant<Int64Range, long, Infinity>(s, out result);
    }
}

public abstract partial record DecimalRange
{
    public sealed partial record EmptyRange
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="EmptyRange"/>, using the same invariant
        /// grammar as <see cref="DecimalRange.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new EmptyRange Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<DecimalRange, decimal, EmptyRange>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="EmptyRange"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out EmptyRange? result)
            => RangeFormat.TryParseVariant<DecimalRange, decimal, EmptyRange>(s, out result);
    }

    public sealed partial record Finite
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="Finite"/>, using the same invariant
        /// grammar as <see cref="DecimalRange.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new Finite Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<DecimalRange, decimal, Finite>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="Finite"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out Finite? result)
            => RangeFormat.TryParseVariant<DecimalRange, decimal, Finite>(s, out result);
    }

    public sealed partial record UnboundedStart
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="UnboundedStart"/>, using the same invariant
        /// grammar as <see cref="DecimalRange.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new UnboundedStart Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<DecimalRange, decimal, UnboundedStart>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="UnboundedStart"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out UnboundedStart? result)
            => RangeFormat.TryParseVariant<DecimalRange, decimal, UnboundedStart>(s, out result);
    }

    public sealed partial record UnboundedEnd
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="UnboundedEnd"/>, using the same invariant
        /// grammar as <see cref="DecimalRange.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new UnboundedEnd Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<DecimalRange, decimal, UnboundedEnd>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="UnboundedEnd"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out UnboundedEnd? result)
            => RangeFormat.TryParseVariant<DecimalRange, decimal, UnboundedEnd>(s, out result);
    }

    public sealed partial record Infinity
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="Infinity"/>, using the same invariant
        /// grammar as <see cref="DecimalRange.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new Infinity Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<DecimalRange, decimal, Infinity>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="Infinity"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out Infinity? result)
            => RangeFormat.TryParseVariant<DecimalRange, decimal, Infinity>(s, out result);
    }
}

public abstract partial record Float64Range
{
    public sealed partial record EmptyRange
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="EmptyRange"/>, using the same invariant
        /// grammar as <see cref="Float64Range.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new EmptyRange Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<Float64Range, double, EmptyRange>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="EmptyRange"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out EmptyRange? result)
            => RangeFormat.TryParseVariant<Float64Range, double, EmptyRange>(s, out result);
    }

    public sealed partial record Finite
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="Finite"/>, using the same invariant
        /// grammar as <see cref="Float64Range.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new Finite Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<Float64Range, double, Finite>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="Finite"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out Finite? result)
            => RangeFormat.TryParseVariant<Float64Range, double, Finite>(s, out result);
    }

    public sealed partial record UnboundedStart
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="UnboundedStart"/>, using the same invariant
        /// grammar as <see cref="Float64Range.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new UnboundedStart Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<Float64Range, double, UnboundedStart>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="UnboundedStart"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out UnboundedStart? result)
            => RangeFormat.TryParseVariant<Float64Range, double, UnboundedStart>(s, out result);
    }

    public sealed partial record UnboundedEnd
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="UnboundedEnd"/>, using the same invariant
        /// grammar as <see cref="Float64Range.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new UnboundedEnd Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<Float64Range, double, UnboundedEnd>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="UnboundedEnd"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out UnboundedEnd? result)
            => RangeFormat.TryParseVariant<Float64Range, double, UnboundedEnd>(s, out result);
    }

    public sealed partial record Infinity
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="Infinity"/>, using the same invariant
        /// grammar as <see cref="Float64Range.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new Infinity Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<Float64Range, double, Infinity>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="Infinity"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out Infinity? result)
            => RangeFormat.TryParseVariant<Float64Range, double, Infinity>(s, out result);
    }
}

public abstract partial record Float32Range
{
    public sealed partial record EmptyRange
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="EmptyRange"/>, using the same invariant
        /// grammar as <see cref="Float32Range.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new EmptyRange Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<Float32Range, float, EmptyRange>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="EmptyRange"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out EmptyRange? result)
            => RangeFormat.TryParseVariant<Float32Range, float, EmptyRange>(s, out result);
    }

    public sealed partial record Finite
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="Finite"/>, using the same invariant
        /// grammar as <see cref="Float32Range.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new Finite Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<Float32Range, float, Finite>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="Finite"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out Finite? result)
            => RangeFormat.TryParseVariant<Float32Range, float, Finite>(s, out result);
    }

    public sealed partial record UnboundedStart
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="UnboundedStart"/>, using the same invariant
        /// grammar as <see cref="Float32Range.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new UnboundedStart Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<Float32Range, float, UnboundedStart>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="UnboundedStart"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out UnboundedStart? result)
            => RangeFormat.TryParseVariant<Float32Range, float, UnboundedStart>(s, out result);
    }

    public sealed partial record UnboundedEnd
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="UnboundedEnd"/>, using the same invariant
        /// grammar as <see cref="Float32Range.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new UnboundedEnd Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<Float32Range, float, UnboundedEnd>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="UnboundedEnd"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out UnboundedEnd? result)
            => RangeFormat.TryParseVariant<Float32Range, float, UnboundedEnd>(s, out result);
    }

    public sealed partial record Infinity
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="Infinity"/>, using the same invariant
        /// grammar as <see cref="Float32Range.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new Infinity Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<Float32Range, float, Infinity>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="Infinity"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out Infinity? result)
            => RangeFormat.TryParseVariant<Float32Range, float, Infinity>(s, out result);
    }
}

public abstract partial record DateRange
{
    public sealed partial record EmptyRange
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="EmptyRange"/>, using the same invariant
        /// grammar as <see cref="DateRange.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new EmptyRange Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<DateRange, DateOnly, EmptyRange>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="EmptyRange"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out EmptyRange? result)
            => RangeFormat.TryParseVariant<DateRange, DateOnly, EmptyRange>(s, out result);
    }

    public sealed partial record Finite
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="Finite"/>, using the same invariant
        /// grammar as <see cref="DateRange.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new Finite Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<DateRange, DateOnly, Finite>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="Finite"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out Finite? result)
            => RangeFormat.TryParseVariant<DateRange, DateOnly, Finite>(s, out result);
    }

    public sealed partial record UnboundedStart
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="UnboundedStart"/>, using the same invariant
        /// grammar as <see cref="DateRange.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new UnboundedStart Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<DateRange, DateOnly, UnboundedStart>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="UnboundedStart"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out UnboundedStart? result)
            => RangeFormat.TryParseVariant<DateRange, DateOnly, UnboundedStart>(s, out result);
    }

    public sealed partial record UnboundedEnd
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="UnboundedEnd"/>, using the same invariant
        /// grammar as <see cref="DateRange.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new UnboundedEnd Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<DateRange, DateOnly, UnboundedEnd>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="UnboundedEnd"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out UnboundedEnd? result)
            => RangeFormat.TryParseVariant<DateRange, DateOnly, UnboundedEnd>(s, out result);
    }

    public sealed partial record Infinity
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="Infinity"/>, using the same invariant
        /// grammar as <see cref="DateRange.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new Infinity Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<DateRange, DateOnly, Infinity>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="Infinity"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out Infinity? result)
            => RangeFormat.TryParseVariant<DateRange, DateOnly, Infinity>(s, out result);
    }
}

public abstract partial record DateTimeRange
{
    public sealed partial record EmptyRange
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="EmptyRange"/>, using the same invariant
        /// grammar as <see cref="DateTimeRange.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new EmptyRange Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<DateTimeRange, DateTime, EmptyRange>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="EmptyRange"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out EmptyRange? result)
            => RangeFormat.TryParseVariant<DateTimeRange, DateTime, EmptyRange>(s, out result);
    }

    public sealed partial record Finite
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="Finite"/>, using the same invariant
        /// grammar as <see cref="DateTimeRange.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new Finite Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<DateTimeRange, DateTime, Finite>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="Finite"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out Finite? result)
            => RangeFormat.TryParseVariant<DateTimeRange, DateTime, Finite>(s, out result);
    }

    public sealed partial record UnboundedStart
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="UnboundedStart"/>, using the same invariant
        /// grammar as <see cref="DateTimeRange.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new UnboundedStart Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<DateTimeRange, DateTime, UnboundedStart>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="UnboundedStart"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out UnboundedStart? result)
            => RangeFormat.TryParseVariant<DateTimeRange, DateTime, UnboundedStart>(s, out result);
    }

    public sealed partial record UnboundedEnd
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="UnboundedEnd"/>, using the same invariant
        /// grammar as <see cref="DateTimeRange.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new UnboundedEnd Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<DateTimeRange, DateTime, UnboundedEnd>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="UnboundedEnd"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out UnboundedEnd? result)
            => RangeFormat.TryParseVariant<DateTimeRange, DateTime, UnboundedEnd>(s, out result);
    }

    public sealed partial record Infinity
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="Infinity"/>, using the same invariant
        /// grammar as <see cref="DateTimeRange.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new Infinity Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<DateTimeRange, DateTime, Infinity>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="Infinity"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out Infinity? result)
            => RangeFormat.TryParseVariant<DateTimeRange, DateTime, Infinity>(s, out result);
    }
}

public abstract partial record DateTimeOffsetRange
{
    public sealed partial record EmptyRange
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="EmptyRange"/>, using the same invariant
        /// grammar as <see cref="DateTimeOffsetRange.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new EmptyRange Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<DateTimeOffsetRange, DateTimeOffset, EmptyRange>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="EmptyRange"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out EmptyRange? result)
            => RangeFormat.TryParseVariant<DateTimeOffsetRange, DateTimeOffset, EmptyRange>(s, out result);
    }

    public sealed partial record Finite
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="Finite"/>, using the same invariant
        /// grammar as <see cref="DateTimeOffsetRange.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new Finite Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<DateTimeOffsetRange, DateTimeOffset, Finite>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="Finite"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out Finite? result)
            => RangeFormat.TryParseVariant<DateTimeOffsetRange, DateTimeOffset, Finite>(s, out result);
    }

    public sealed partial record UnboundedStart
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="UnboundedStart"/>, using the same invariant
        /// grammar as <see cref="DateTimeOffsetRange.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new UnboundedStart Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<DateTimeOffsetRange, DateTimeOffset, UnboundedStart>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="UnboundedStart"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out UnboundedStart? result)
            => RangeFormat.TryParseVariant<DateTimeOffsetRange, DateTimeOffset, UnboundedStart>(s, out result);
    }

    public sealed partial record UnboundedEnd
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="UnboundedEnd"/>, using the same invariant
        /// grammar as <see cref="DateTimeOffsetRange.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new UnboundedEnd Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<DateTimeOffsetRange, DateTimeOffset, UnboundedEnd>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="UnboundedEnd"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out UnboundedEnd? result)
            => RangeFormat.TryParseVariant<DateTimeOffsetRange, DateTimeOffset, UnboundedEnd>(s, out result);
    }

    public sealed partial record Infinity
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="Infinity"/>, using the same invariant
        /// grammar as <see cref="DateTimeOffsetRange.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new Infinity Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<DateTimeOffsetRange, DateTimeOffset, Infinity>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="Infinity"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out Infinity? result)
            => RangeFormat.TryParseVariant<DateTimeOffsetRange, DateTimeOffset, Infinity>(s, out result);
    }
}

public abstract partial record TimeRange
{
    public sealed partial record EmptyRange
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="EmptyRange"/>, using the same invariant
        /// grammar as <see cref="TimeRange.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new EmptyRange Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<TimeRange, TimeOnly, EmptyRange>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="EmptyRange"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out EmptyRange? result)
            => RangeFormat.TryParseVariant<TimeRange, TimeOnly, EmptyRange>(s, out result);
    }

    public sealed partial record Finite
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="Finite"/>, using the same invariant
        /// grammar as <see cref="TimeRange.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new Finite Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<TimeRange, TimeOnly, Finite>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="Finite"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out Finite? result)
            => RangeFormat.TryParseVariant<TimeRange, TimeOnly, Finite>(s, out result);
    }

    public sealed partial record UnboundedStart
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="UnboundedStart"/>, using the same invariant
        /// grammar as <see cref="TimeRange.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new UnboundedStart Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<TimeRange, TimeOnly, UnboundedStart>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="UnboundedStart"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out UnboundedStart? result)
            => RangeFormat.TryParseVariant<TimeRange, TimeOnly, UnboundedStart>(s, out result);
    }

    public sealed partial record UnboundedEnd
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="UnboundedEnd"/>, using the same invariant
        /// grammar as <see cref="TimeRange.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new UnboundedEnd Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<TimeRange, TimeOnly, UnboundedEnd>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="UnboundedEnd"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out UnboundedEnd? result)
            => RangeFormat.TryParseVariant<TimeRange, TimeOnly, UnboundedEnd>(s, out result);
    }

    public sealed partial record Infinity
    {
        /// <summary>
        /// Parses a PostgreSQL range literal that names a <see cref="Infinity"/>, using the same invariant
        /// grammar as <see cref="TimeRange.Parse(string, IFormatProvider?)"/>.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <exception cref="FormatException">The literal does not parse, or names another shape.</exception>
        public static new Infinity Parse(string s, IFormatProvider? provider)
            => RangeFormat.ParseVariant<TimeRange, TimeOnly, Infinity>(s);

        /// <summary>
        /// Tries to parse a PostgreSQL range literal that names a <see cref="Infinity"/>. Minimal APIs bind
        /// a parameter of this type through it.
        /// </summary>
        /// <param name="s">The literal.</param>
        /// <param name="provider">Ignored: the literal is invariant.</param>
        /// <param name="result">The range, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the literal does not parse or names another shape.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out Infinity? result)
            => RangeFormat.TryParseVariant<TimeRange, TimeOnly, Infinity>(s, out result);
    }
}
