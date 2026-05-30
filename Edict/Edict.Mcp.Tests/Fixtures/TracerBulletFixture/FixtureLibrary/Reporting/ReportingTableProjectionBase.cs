using Edict.Contracts.Persistence;
using Edict.Core.Projections;

namespace FixtureLibrary.Reporting;

public abstract class ReportingTableProjectionBase<TRow> : EdictTableProjectionBuilder<TRow>
    where TRow : class, IEdictPersistedState, new();
