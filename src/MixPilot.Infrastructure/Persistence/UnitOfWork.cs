using Microsoft.EntityFrameworkCore;
using MixPilot.Application.Abstractions.Persistence;

namespace MixPilot.Infrastructure.Persistence;

public sealed class UnitOfWork(MixPilotDbContext context) : IUnitOfWork
{
    public async Task ExecuteInTransaction(Func<CancellationToken, Task> operation, CancellationToken ct)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        try
        {
            await operation(ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            throw;
        }
    }
}
