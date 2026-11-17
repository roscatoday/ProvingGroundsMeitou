using System.Reflection;
using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>Task 9.2b: the methods the JIT warm-up (src/Engine/PgJitWarmup.cs) compiles ahead: every non-generic method of the assemblies, and the
/// methods of their closed generic types that the fields name, with the type's instantiation; never an open, abstract or generic method.</summary>
public class JitWarmupListTests
{
    // a type of another assembly (this one) naming Core's generic types in its fields, as the mod's glue does with CharacterId
#pragma warning disable CS0169, CS0649   // the fields are only read by reflection
    sealed class Holder
    {
        TownArenaLogic<long>? logic;
        List<ArenaRowView<long>>? rows;
        TickCostWindow[]? windows;
    }
#pragma warning restore CS0169, CS0649

    static readonly Assembly CoreAssembly = typeof(TickCostWindow).Assembly;

    [Fact]
    public void ListsTheNonGenericMethodsWithoutAnInstantiation()
    {
        var list = JitWarmupList.Methods([CoreAssembly]);
        var add = typeof(TickCostWindow).GetMethod(nameof(TickCostWindow.Add))!;
        Assert.Contains(list, e => e.Method == add && e.Instantiation is null);
        Assert.Contains(list, e => e.Method is ConstructorInfo c && c.DeclaringType == typeof(TickCostWindow));
        Assert.All(list, e => Assert.False(e.Method.IsAbstract || e.Method.ContainsGenericParameters));
    }

    [Fact]
    public void NeverListsATypeInitializer()
    {
        // Core has types with static field initializers (a type initializer each); none of them may be listed, nor one of a closed generic type
        Assert.Contains(CoreAssembly.GetTypes(), t => t.TypeInitializer is not null);
        var list = JitWarmupList.Methods([CoreAssembly]);
        Assert.DoesNotContain(list, e => e.Method is ConstructorInfo { IsStatic: true });
        Assert.DoesNotContain(JitWarmupList.Methods([typeof(Holder)], new HashSet<Assembly> { CoreAssembly }), e => e.Method is ConstructorInfo { IsStatic: true });
    }

    [Fact]
    public void FindsTheClosedGenericTypesTheFieldsName()
    {
        var closed = JitWarmupList.ClosedGenerics([typeof(Holder)], new HashSet<Assembly> { CoreAssembly });
        Assert.Contains(typeof(TownArenaLogic<long>), closed);
        Assert.Contains(typeof(ArenaRowView<long>), closed);   // through List<>'s argument
        Assert.DoesNotContain(typeof(List<ArenaRowView<long>>), closed);   // not ours: left to the runtime
        Assert.All(closed, t => Assert.False(t.ContainsGenericParameters));
    }

    [Fact]
    public void AClosedGenericTypesMethodsCarryItsInstantiation()
    {
        var list = JitWarmupList.Methods([typeof(Holder)], new HashSet<Assembly> { CoreAssembly });
        var tick = typeof(TownArenaLogic<long>).GetMethod(nameof(TownArenaLogic<long>.Tick))!;
        var entry = Assert.Single(list, e => e.Method == tick);
        Assert.NotNull(entry.Instantiation);
        Assert.Equal([typeof(long).TypeHandle], entry.Instantiation!);
        Assert.Contains(list, e => e.Method is ConstructorInfo c && c.DeclaringType == typeof(Holder) && e.Instantiation is null);
        Assert.All(list, e => Assert.False(e.Method.IsAbstract || e.Method.ContainsGenericParameters));
    }
}
