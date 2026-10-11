using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Query;

/// <summary>
/// Wraps the provider's <see cref="IRelationalParameterBasedSqlProcessorFactory"/> so that every
/// processor it creates runs <see cref="ValueSetSqlGuard"/> over its result — which is also what
/// turns a translated <c>Union</c> into <c>array_cat</c>, so a query that bypasses this decorator
/// fails on the server instead of skipping the guard.
/// </summary>
/// <remarks>
/// <para>
/// The parameter-based processor is the last step before SQL generation, and the only one every
/// command passes through: queries, <c>ExecuteUpdate</c> and <c>ExecuteDelete</c> each build a
/// <c>RelationalCommandCache</c> that calls it. The tree it returns is final — projections applied,
/// null semantics expanded — so the guard judges exactly what will be sent.
/// </para>
/// <para>
/// EF Core offers no plugin seam at this stage, so the provider's registration is decorated rather
/// than replaced (see <see cref="Decorate"/>): the provider's own processing runs unchanged, and the
/// guard only reads its output. No EF-internal API is involved — the overridden
/// <c>Process(Expression, ParametersCacheDecorator)</c> is the public extension point.
/// </para>
/// </remarks>
internal sealed class ValueRangesParameterBasedSqlProcessorFactory(
    IRelationalParameterBasedSqlProcessorFactory   inner,
    RelationalParameterBasedSqlProcessorDependencies dependencies
) : IRelationalParameterBasedSqlProcessorFactory
{
    /// <inheritdoc />
    public RelationalParameterBasedSqlProcessor Create(RelationalParameterBasedSqlProcessorParameters parameters)
        => new GuardedProcessor(inner.Create(parameters), dependencies, parameters);

    /// <summary>
    /// Replaces the registered <see cref="IRelationalParameterBasedSqlProcessorFactory"/> with this
    /// decorator around it. Idempotent; registers the Npgsql provider services first when they are
    /// not there yet, so there is always a provider factory to wrap.
    /// </summary>
    internal static void Decorate(IServiceCollection services)
    {
        var existing = services.LastOrDefault(d => d.ServiceType == typeof(IRelationalParameterBasedSqlProcessorFactory));
        if (existing is null)
        {
            services.AddEntityFrameworkNpgsql();
            existing = services.Last(d => d.ServiceType == typeof(IRelationalParameterBasedSqlProcessorFactory));
        }

        if (existing.ImplementationFactory?.Target is Decoration) return;

        services.Remove(existing);
        services.Add(new ServiceDescriptor(
            typeof(IRelationalParameterBasedSqlProcessorFactory),
            new Decoration(existing).Create,
            existing.Lifetime));
    }

    /// <summary>Builds the decorator around whatever the replaced descriptor would have produced.</summary>
    private sealed class Decoration(ServiceDescriptor inner)
    {
        public object Create(IServiceProvider provider)
            => new ValueRangesParameterBasedSqlProcessorFactory(
                (IRelationalParameterBasedSqlProcessorFactory) (
                    inner.ImplementationInstance
                 ?? inner.ImplementationFactory?.Invoke(provider)
                 ?? ActivatorUtilities.CreateInstance(provider, inner.ImplementationType!)),
                provider.GetRequiredService<RelationalParameterBasedSqlProcessorDependencies>());
    }

    private sealed class GuardedProcessor(
        RelationalParameterBasedSqlProcessor            inner,
        RelationalParameterBasedSqlProcessorDependencies dependencies,
        RelationalParameterBasedSqlProcessorParameters   parameters
    ) : RelationalParameterBasedSqlProcessor(dependencies, parameters)
    {
        public override Expression Process(Expression queryExpression, ParametersCacheDecorator parametersDecorator)
            => ValueSetSqlGuard.Check(inner.Process(queryExpression, parametersDecorator));
    }
}
