using System.Diagnostics;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Query;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql.EntityFrameworkCore.PostgreSQL.Query;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.Tests;

/// <summary>
/// An overload the translators recognize by name, with an operand type no arm was written for, is
/// refused with the overload named, never translated as the nearest type it might be (#295).
/// </summary>
/// <remarks>
/// No such overload exists, which is the point: each test hands a translator the declaring type, name and
/// parameters an overload added later would have. Before #295, <c>RangeSet.Contains(StringSet)</c> became
/// <c>@&gt;</c> over a value set mapped as a range. <c>TranslatorDispatchSweepTests</c> (conventions)
/// hands the translators every overload that does exist. The query interceptor's
/// <see cref="RangeSetContains.Equivalent"/> is held the same way (#320).
/// </remarks>
[TestClass]
public sealed class MissingArmTests
{
    private static readonly TestDbContext Context = new();

    private static NpgsqlSqlExpressionFactory Factory => (NpgsqlSqlExpressionFactory)Context.GetService<ISqlExpressionFactory>();

    private static IRelationalTypeMappingSource Mappings => Context.GetService<IRelationalTypeMappingSource>();

    private static IDiagnosticsLogger<DbLoggerCategory.Query> Logger => Context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>();

    [ClassInitialize]
    public static void RegisterTheTypes(TestContext testContext) => _ = Context.Model;

    [TestMethod]
    [DataRow(nameof(RangeSet<,>.Contains), typeof(bool))]
    [DataRow(nameof(RangeSet<,>.Union), typeof(RangeSet<DateRange, DateOnly>))]
    public void RangeSetCall_WithAnOperandOfAnotherType_IsRefusedNamingIt(string name, Type returns)
    {
        var overload   = new Overload(typeof(RangeSet<DateRange, DateOnly>), name, returns, isStatic: false, typeof(StringSet));
        var translator = new ValueRangesMethodCallTranslator(Factory, Mappings);

        var refusal = Assert.ThrowsExactly<UnreachableException>(
            () => translator.Translate(Operand(typeof(RangeSet<DateRange, DateOnly>)), overload, [Operand(typeof(StringSet))], Logger));

        StringAssert.Contains(refusal.Message, $"RangeSet<DateRange, DateOnly>.{name}(StringSet) has no translation for an operand of type StringSet");
    }

    [TestMethod]
    public void Factory_WithOperandsOfAnotherType_IsRefusedNamingIt()
    {
        var overload   = new Overload(typeof(DateRange), nameof(DateRange.CreateFinite), typeof(DateRange), isStatic: true, typeof(int), typeof(int), typeof(bool), typeof(bool));
        var translator = new ValueRangesMethodCallTranslator(Factory, Mappings);

        var refusal = Assert.ThrowsExactly<UnreachableException>(
            () => translator.Translate(null, overload, [Operand(typeof(int)), Operand(typeof(int)), Factory.Constant(true), Factory.Constant(false)], Logger));

        StringAssert.Contains(refusal.Message, "DateRange.CreateFinite(Int32, Int32, Boolean, Boolean) has no translation");
    }

    [TestMethod]
    [DataRow(nameof(ValueSetExtensions.Overlaps), typeof(Int32Set))]
    [DataRow(nameof(ValueSetExtensions.Contains), typeof(int))]
    public void ValueSetCall_WithAnOperandOfAnotherType_IsRefusedNamingIt(string name, Type operand)
    {
        var overload   = new Overload(typeof(ValueSetExtensions), name, typeof(bool), isStatic: true, typeof(StringSet), operand);
        var translator = new ValueSetsMethodCallTranslator(Factory, Mappings);

        var refusal = Assert.ThrowsExactly<UnreachableException>(
            () => translator.Translate(null, overload, [Operand(typeof(StringSet)), Operand(operand)], Logger));

        StringAssert.Contains(refusal.Message, $"has no translation for an operand of type {operand.Name}");
    }

    [TestMethod]
    public void Aggregate_OverASourceOfAnotherType_IsRefusedNamingIt()
    {
        var overload   = new Overload(typeof(RangeAggregateExtensions), nameof(RangeAggregateExtensions.RangeAgg), typeof(RangeSet<DateRange, DateOnly>),
                                      isStatic: true, typeof(IEnumerable<IRange<DateOnly>>));
        var translator = new ValueRangesAggregateMethodCallTranslator(Factory);

        var refusal = Assert.ThrowsExactly<UnreachableException>(
            () => translator.Translate(overload, new EnumerableExpression(Operand(typeof(IRange<DateOnly>))), [], Logger));

        StringAssert.Contains(refusal.Message, "has no translation for an operand of type IRange<DateOnly>");
    }

    [TestMethod]
    public void Member_OfARecognizedNameAnsweringAnotherType_IsDeclined()
    {
        var translator = new ValueSetsMemberTranslator(Factory);

        Assert.IsNull(translator.Translate(Operand(typeof(StringSet)), new Property(typeof(StringSet), nameof(StringSet.Count), typeof(long)), typeof(long), Logger));
    }

    /// <summary>
    /// Every public one-argument <c>Contains</c> on a <see cref="RangeSet{TRange,T}"/> has a static form taking
    /// the same operand, so the interceptor never hands one overload's operand to another's (#320). An overload
    /// added later fails here, not in a query.
    /// </summary>
    [TestMethod]
    [DataRow(typeof(RangeSet<DateRange, DateOnly>))]
    [DataRow(typeof(RangeSet<Int32Range, int>))]
    [DataRow(typeof(RangeSet<TimeRange, TimeOnly>))]
    public void RangeSetContains_MapsEveryOverloadToAStaticFormTakingItsOperand(Type set)
    {
        var overloads = set.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                           .Where(method => method.Name == nameof(RangeSet<,>.Contains) && method.GetParameters().Length == 1)
                           .ToList();

        Assert.IsGreaterThanOrEqualTo(3, overloads.Count, $"Expected the three Contains overloads on {set.Name}, found {overloads.Count}.");
        foreach (var overload in overloads)
        {
            var equivalent = RangeSetContains.Equivalent(overload);

            Assert.IsNotNull(equivalent, $"{overload} has no static form.");
            CollectionAssert.AreEqual(
                new[] { set, overload.GetParameters()[0].ParameterType },
                equivalent.GetParameters().Select(parameter => parameter.ParameterType).ToArray(),
                $"{overload} maps to {equivalent}.");
        }
    }

    [TestMethod]
    public void RangeSetContains_WithAnOperandOfAnotherType_IsRefusedNamingIt()
    {
        var overload = new Overload(typeof(RangeSet<DateRange, DateOnly>), nameof(RangeSet<,>.Contains), typeof(bool), isStatic: false, typeof(StringSet));

        var refusal = Assert.ThrowsExactly<UnreachableException>(() => RangeSetContains.Equivalent(overload));

        StringAssert.Contains(refusal.Message, "RangeSet<DateRange, DateOnly>.Contains(StringSet) has no translation for an operand of type StringSet");
    }

    private static SqlParameterExpression Operand(Type type) => new($"operand_{type.Name}", type, typeMapping: null);

    /// <summary>A method with the declaring type, name and parameters an overload added later would have.</summary>
    private sealed class Overload(Type declaringType, string name, Type returns, bool isStatic, params Type[] parameterTypes) : MethodInfo
    {
        public override Type DeclaringType => declaringType;

        public override Type ReflectedType => declaringType;

        public override string Name => name;

        public override Type ReturnType => returns;

        public override MethodAttributes Attributes => MethodAttributes.Public | (isStatic ? MethodAttributes.Static : 0);

        public override ParameterInfo[] GetParameters() => [.. parameterTypes.Select(type => new Parameter(type, this))];

        public override RuntimeMethodHandle MethodHandle => throw new NotSupportedException();

        public override ICustomAttributeProvider ReturnTypeCustomAttributes => throw new NotSupportedException();

        public override MethodInfo GetBaseDefinition() => this;

        public override MethodImplAttributes GetMethodImplementationFlags() => MethodImplAttributes.IL;

        public override object Invoke(object? obj, BindingFlags invokeAttr, Binder? binder, object?[]? parameters, CultureInfo? culture) => throw new NotSupportedException();

        public override object[] GetCustomAttributes(bool inherit) => [];

        public override object[] GetCustomAttributes(Type attributeType, bool inherit) => [];

        public override bool IsDefined(Type attributeType, bool inherit) => false;
    }

    private sealed class Parameter(Type type, MemberInfo member) : ParameterInfo
    {
        public override Type ParameterType => type;

        public override MemberInfo Member => member;
    }

    /// <summary>A property with the declaring type, name and type a member added later would have.</summary>
    private sealed class Property(Type declaringType, string name, Type type) : PropertyInfo
    {
        public override Type DeclaringType => declaringType;

        public override Type ReflectedType => declaringType;

        public override string Name => name;

        public override Type PropertyType => type;

        public override PropertyAttributes Attributes => PropertyAttributes.None;

        public override bool CanRead => true;

        public override bool CanWrite => false;

        public override MethodInfo[] GetAccessors(bool nonPublic) => [];

        public override MethodInfo? GetGetMethod(bool nonPublic) => null;

        public override MethodInfo? GetSetMethod(bool nonPublic) => null;

        public override ParameterInfo[] GetIndexParameters() => [];

        public override object GetValue(object? obj, BindingFlags invokeAttr, Binder? binder, object?[]? index, CultureInfo? culture) => throw new NotSupportedException();

        public override void SetValue(object? obj, object? value, BindingFlags invokeAttr, Binder? binder, object?[]? index, CultureInfo? culture) => throw new NotSupportedException();

        public override object[] GetCustomAttributes(bool inherit) => [];

        public override object[] GetCustomAttributes(Type attributeType, bool inherit) => [];

        public override bool IsDefined(Type attributeType, bool inherit) => false;
    }
}
