using Edict.Contracts.Persistence;
using Edict.Core.Projections;

namespace FixtureLibrary.Reporting;

public abstract class ReportingProjectionBase<TRow> : EdictListProjectionBuilder<TRow>
    where TRow : class, IEdictPersistedState, new();
