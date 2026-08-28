namespace PlanFlow.Application.Common.Interfaces;

/// <summary>Hashes and verifies user passwords. Implemented in Infrastructure so Application never touches a crypto library directly.</summary>
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string passwordHash, string password);
}
