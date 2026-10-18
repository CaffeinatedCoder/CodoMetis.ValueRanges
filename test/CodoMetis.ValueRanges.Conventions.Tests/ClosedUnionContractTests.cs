using System.Reflection;
using System.Reflection.Emit;
using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// Every range union refuses a subtype declared outside it, at construction.
/// </summary>
/// <remarks>
/// <para>
/// The documentation promises that the five nested variants are the only ranges that can exist,
/// so a consumer's five-arm switch is complete in fact. The private constructor was credited for
/// that, and it never delivered it: the compiler synthesizes a <b>protected</b> copy constructor on
/// every non-sealed record for <c>with</c> expressions, and refuses to let it be private (CS8878).
/// A record anywhere could chain to it — <c>record Rogue(Int32Range Seed) : Int32Range(Seed)</c> —
/// and become a sixth variant that implements no shape interface, which <c>IsEmpty()</c>,
/// <c>Contains</c> and <c>Overlaps</c> answered <see langword="false"/> rather than throwing (#206).
/// </para>
/// <para>
/// Since 10.0.0 each union declares the copy constructor itself and refuses any runtime type that
/// is not nested in the union. This class plays the consumer against every discovered union: it
/// emits a derived type whose constructor chains to the copy constructor, exactly as the compiler
/// would for the record above, and expects construction to throw. The hand-written
/// <see cref="Rogue"/> is the same thing in source, for the one union a reader can see. The
/// <c>with</c> path the copy constructor exists for is checked on every variant of every union,
/// so a seal that also refused the real variants would fail here too.
/// </para>
/// </remarks>
[TestClass]
public sealed class ClosedUnionContractTests
{
    /// <summary>8 core unions and 3 NodaTime unions, as of 10.0.0.</summary>
    private const int KnownUnions = 11;

    /// <summary>The consumer's sixth variant, as the compiler accepts it.</summary>
    private sealed record Rogue(Int32Range Seed) : Int32Range(Seed);

    [TestMethod]
    public void Int32Range_RefusesASubtypeDeclaredInAnotherAssembly()
    {
        var thrown = Assert.ThrowsExactly<InvalidOperationException>(() => new Rogue(Int32Range.CreateFinite(1, 10)));

        StringAssert.Contains(thrown.Message, nameof(Rogue));
        StringAssert.Contains(thrown.Message, nameof(Int32Range));
    }

    [TestMethod]
    public void EveryUnion_RefusesAnEmittedSubtype_AndStillCopiesItsOwnVariants()
    {
        var checkedUnions = RangeProbes.AllRangeTypes()
                                       .Select(pair => Reflect.InvokeGeneric<string>(
                                            typeof(ClosedUnionContractTests), nameof(AssertClosed),
                                            pair.Range, pair.Element))
                                       .ToList();

        Assert.IsTrue(
            checkedUnions.Count >= KnownUnions,
            $"Only {checkedUnions.Count} unions were checked, fewer than the {KnownUnions} known to exist: "
          + $"{string.Join(", ", checkedUnions)}.");
    }

    private static string AssertClosed<TRange, T>()
        where TRange : class, IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var union = typeof(TRange);
        var name  = union.Name;

        var copyConstructor = union.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, [union]);

        Assert.IsNotNull(copyConstructor, $"{name} has no copy constructor; a record always has one.");
        Assert.IsTrue(copyConstructor.IsFamily, $"{name}'s copy constructor is {copyConstructor.Attributes}; a record's must be protected.");

        // The seal: a type nested nowhere near the union, chaining to the copy constructor the way
        // the compiler chains a derived record's primary constructor.
        var rogue = EmitSubtype(union, copyConstructor);

        foreach (var seed in RangeProbes.Shapes<TRange, T>())
        {
            var thrown = Assert.ThrowsExactly<TargetInvocationException>(
                () => Activator.CreateInstance(rogue, seed),
                $"{name}: a subtype declared outside the union was constructed from {seed.GetType().Name}; the union is not closed.");

            Assert.IsInstanceOfType<InvalidOperationException>(
                thrown.InnerException,
                $"{name}: the copy constructor refused the subtype with {thrown.InnerException?.GetType().Name}, not InvalidOperationException.");
            StringAssert.Contains(thrown.InnerException.Message, name);
        }

        // The door the copy constructor is for: `with` on each real variant, through the method
        // the compiler emits for it, must still produce an equal value of the same variant.
        foreach (var value in RangeProbes.Shapes<TRange, T>())
        {
            var variant = value.GetType();
            var clone   = variant.GetMethod("<Clone>$", BindingFlags.Instance | BindingFlags.Public);

            Assert.IsNotNull(clone, $"{name}.{variant.Name} has no <Clone>$ method; a record always has one.");

            var copy = clone.Invoke(value, null);

            Assert.IsNotNull(copy, $"{name}.{variant.Name}: with produced null.");
            Assert.AreEqual(variant, copy.GetType(), $"{name}.{variant.Name}: with produced a {copy.GetType().Name}.");
            Assert.AreEqual(value, copy, $"{name}.{variant.Name}: with produced a different value.");
        }

        return name;
    }

    private static Type EmitSubtype(Type union, ConstructorInfo copyConstructor)
    {
        var module = AssemblyBuilder
                    .DefineDynamicAssembly(new AssemblyName($"Rogue.{union.Name}"), AssemblyBuilderAccess.Run)
                    .DefineDynamicModule("Rogue");

        var type = module.DefineType($"Rogue{union.Name}", TypeAttributes.Public | TypeAttributes.Class, union);

        var constructor = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [union]);
        var il          = constructor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, copyConstructor);
        il.Emit(OpCodes.Ret);

        // An abstract record's <Clone>$ is abstract; the compiler implements it on a derived
        // record as `new Derived(this)`, which is what a `with` on the rogue would run.
        var baseClone = union.GetMethod("<Clone>$", BindingFlags.Instance | BindingFlags.Public)!;
        var clone     = type.DefineMethod(
            baseClone.Name, MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            baseClone.ReturnType, Type.EmptyTypes);
        il = clone.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, constructor);
        il.Emit(OpCodes.Ret);

        return type.CreateType();
    }
}
