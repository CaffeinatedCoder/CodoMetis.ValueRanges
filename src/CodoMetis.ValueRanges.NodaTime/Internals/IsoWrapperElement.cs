using System.Globalization;
using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges.Internals;

/// <summary>
/// The construction-time check the calendar-bearing wrapper arities — <c>LocalDateSet&lt;T&gt;</c>,
/// <c>LocalDateTimeSet&lt;T&gt;</c>, <c>YearMonthSet&lt;T&gt;</c> — apply to every element: its ISO
/// text form must read back as that same element.
/// </summary>
/// <remarks>
/// <para>
/// NodaTime's ISO patterns format a value in its own calendar's fields. A wrapper over a Julian
/// <c>LocalDate(2024, 1, 1)</c> — ISO 2024-01-14 — therefore wrote <c>2024-01-01</c> into the array
/// literal, the JSON array and the EF Core bridge, all of which read it back as ISO 2024-01-01:
/// thirteen days off, silently. The closed sets convert such a value with <c>WithCalendar</c>; a
/// wrapper arity cannot, because the only way to build a wrapper element from an ISO
/// primitive is to parse it, and the element's calendar is not visible through the BCL interfaces
/// it is constrained on. So the element is refused, as a bare non-ISO value is refused on the JSON
/// write path.
/// </para>
/// <para>
/// The test is the round trip itself — format with the family's pinned pattern, parse back through
/// the family's <c>ParseValue</c>, compare — so it asks exactly the question that matters (would
/// the stored text be this element?) without knowing what makes the element differ. An element
/// whose text does not parse at all (one that ignores the format argument) is left alone here: it
/// already fails loudly at every text boundary, which its own tests pin.
/// </para>
/// </remarks>
internal static class IsoWrapperElement
{
    internal static TElement Require<TSet, TElement>(TElement value)
        where TSet : IValueSetFactory<TSet, TElement>, IValueSet<TElement>
        where TElement : IEquatable<TElement>
    {
        var text = TSet.FormatValue(value, null, CultureInfo.InvariantCulture);

        TElement readBack;
        try
        {
            readBack = TSet.ParseValue(text.AsSpan(), CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
        {
            return value;
        }

        return readBack.Equals(value)
                   ? value
                   : throw new ArgumentException(
                         $"A {typeof(TSet).Name.Split('`')[0]}<{typeof(TElement).Name}> element must be backed by a value in "
                       + $"the ISO calendar. This one formats as '{text}' in the family's ISO 8601 text form, which reads "
                       + "back as a different value — what a NodaTime value in another calendar does, since its own "
                       + "fields are written as if they were ISO. Convert the wrapped value with "
                       + "WithCalendar(CalendarSystem.Iso) before wrapping it.",
                         nameof(value));
    }
}
