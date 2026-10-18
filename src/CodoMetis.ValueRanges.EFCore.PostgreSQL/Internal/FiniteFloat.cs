namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal;

/// <summary>
/// The finite guard of the float families, applied to the values EF Core binds and inlines: an element
/// operand (<c>range @&gt; @p</c>, <c>set @&gt; ARRAY[@p]</c>) and every bound and element of a range or set
/// on its way to the store. The model refuses <c>NaN</c> and the infinities at construction, but a bare
/// <see cref="double"/> operand reaches the provider without passing a factory, so without this a
/// <c>NaN</c> parameter would bind and PostgreSQL would answer for it — <c>[0,) @&gt; 'NaN'</c> is true
/// there and false in memory (#153). Passed as the definitions' <c>normalizeValue</c>, the same check the
/// core types make in <c>NormalizeElement</c>.
/// </summary>
internal static class FiniteFloat
{
    internal static double Require(double value)
        => double.IsFinite(value)
               ? value
               : throw new ArgumentException(
                     $"float8range, float8multirange and double precision[] operands are finite numbers only; got {value}. "
                   + "Float64Range and Float64Set hold no NaN or infinity, so no query over them can be answered for one.",
                     nameof(value));

    internal static float Require(float value)
        => float.IsFinite(value)
               ? value
               : throw new ArgumentException(
                     $"float4range, float4multirange and real[] operands are finite numbers only; got {value}. "
                   + "Float32Range and Float32Set hold no NaN or infinity, so no query over them can be answered for one.",
                     nameof(value));
}
