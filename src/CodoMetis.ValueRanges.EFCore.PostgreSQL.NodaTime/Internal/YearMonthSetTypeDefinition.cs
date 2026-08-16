using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal;
using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Storage;
using Microsoft.EntityFrameworkCore.Storage;
using NodaTime;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.NodaTime.Internal;
// EF1001 here is EF's analyzer reading CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal
// as an internal namespace — it keys on the `EntityFrameworkCore.*.Internal` shape, not on an
// attribute. That warning is aimed at consumers of the plugin; this satellite is the same codebase
// and builds on those types by design.
#pragma warning disable EF1001


/// <summary>
/// Binds <see cref="YearMonthSet"/> to a month-aligned PostgreSQL <c>date[]</c>.
/// <see cref="YearMonth"/> has no wire representation, so every element converts through the
/// first day of its month. Reads validate month alignment — a <c>date[]</c> holding a
/// non-first-of-month date fails loudly rather than silently shifting to its month.
/// </summary>
internal sealed class YearMonthSetTypeDefinition : ISetTypeDefinition
{
    private static LocalDate ToPrimitive(YearMonth element)
        => element.Calendar == CalendarSystem.Iso
               ? element.OnDayOfMonth(1)
               : throw new ArgumentException(
                     $"YearMonthSet elements must be in the ISO calendar; got {element} ({element.Calendar}). "
                   + "A non-ISO year-month spans parts of two ISO months and has no lossless ISO equivalent.");

    private static YearMonth FromPrimitive(LocalDate primitive)
        => primitive.Day == 1
               ? primitive.ToYearMonth()
               : throw new InvalidOperationException(
                     $"A YearMonthSet column must hold first-of-month dates; got {primitive}. "
                   + "The stored array is corrupt for this mapping.");

    public Type SetClrType => typeof(YearMonthSet);

    public Type ElementClrType => typeof(YearMonth);

    public string ElementStoreType => "date";

    public string ArrayStoreType => "date[]";

    public RelationalTypeMapping SetTypeMapping { get; } = new ValueSetTypeMapping<YearMonthSet, YearMonth, LocalDate>(
        "date[]", ToPrimitive, FromPrimitive, global::NodaTime.Text.LocalDatePattern.Iso.Format);

    /// <summary>
    /// The same converting element mapping the range definition uses: a bare
    /// <see cref="YearMonth"/> operand in <c>column @&gt; ARRAY[@p]</c> binds as a
    /// first-of-month <c>date</c>.
    /// </summary>
    public RelationalTypeMapping ElementTypeMapping { get; } = new YearMonthTypeMapping();

    public object EmptySet => YearMonthSet.Empty;
}
