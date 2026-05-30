namespace Stash.Registry.Database.Migrations;

/// <summary>
/// Design-time documentation of the packages uniqueness guarantee.
/// </summary>
/// <remarks>
/// <para>
/// The <c>packages</c> table's <c>name</c> column is its PRIMARY KEY.  The name is
/// always of the form <c>@{scope}/{localName}</c>, so the primary key is already a
/// bijection with the (scope, localName) pair.  No additional UNIQUE constraint is
/// needed for the column — the PK itself is the unique constraint on the namespace.
/// </para>
/// <para>
/// The EF model in <see cref="Stash.Registry.Database.RegistryDbContext"/> is the
/// canonical source of truth.  The <c>EnsureCreated()</c> call in
/// <see cref="Stash.Registry.Database.StashRegistryDatabase.Initialize"/> emits
/// the schema DDL from the EF model on first startup.  A traditional EF migration
/// is not used in this project.
/// </para>
/// <para>
/// <b>Atomicity guarantee (D20):</b>  <see cref="Stash.Registry.Services.PackageService.PublishAsync"/>
/// uses <em>insert-then-handle-unique-violation</em>, never check-then-act.  The service
/// issues the <c>INSERT</c> for a new package row and, on
/// <c>Microsoft.EntityFrameworkCore.DbUpdateException</c> whose inner exception is a
/// SQLite constraint violation (<c>SqliteException.SqliteErrorCode == 19</c> / SQLITE_CONSTRAINT),
/// falls through to the <em>PublishVersion</em> code path on the now-existing row.
/// Under PostgreSQL the inner exception carries error code <c>23505</c> (unique_violation).
/// Two concurrent first-publish requests therefore resolve to exactly one
/// <c>packages</c> row — the loser collapses to a publish-version rather than a
/// duplicate-row error that would surface as 500.
/// </para>
/// </remarks>
public static class AddPackagesUniqueConstraint
{
    /// <summary>
    /// The SQLite constraint error code (<c>SQLITE_CONSTRAINT</c> = 19).
    /// Used by <see cref="Stash.Registry.Services.PackageService"/> to detect a
    /// duplicate-insert and retry as a PublishVersion.
    /// </summary>
    public const int SqliteConstraintErrorCode = 19;

    /// <summary>
    /// The PostgreSQL unique-violation SQLSTATE code.
    /// Reserved for future use when the registry runs on PostgreSQL.
    /// </summary>
    public const string PostgresUniqueViolationCode = "23505";
}
