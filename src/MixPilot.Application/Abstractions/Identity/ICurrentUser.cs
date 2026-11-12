namespace MixPilot.Application.Abstractions.Identity;

public interface ICurrentUser
{
    string OwnerKey { get; }
}

