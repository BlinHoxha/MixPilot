namespace MixPilot.Application.Abstractions.Persistence;

public interface IUnitOfWork
{
    Task ExecuteInTransaction(Func<CancellationToken, Task> operation, CancellationToken ct);
}
