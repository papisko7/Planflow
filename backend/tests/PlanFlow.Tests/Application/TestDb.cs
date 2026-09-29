using Microsoft.EntityFrameworkCore;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Infrastructure.Persistence;

namespace PlanFlow.Tests.Application;

/// <summary>Shared helpers for handler tests: a fresh EF InMemory context and quick user/team seeding.</summary>
internal static class TestDb
{
    public static PlanFlowDbContext Create() =>
        new(new DbContextOptionsBuilder<PlanFlowDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    public static async Task<User> AddUserAsync(PlanFlowDbContext db, string name = "User")
    {
        var user = new User { Email = $"{Guid.NewGuid()}@example.com", DisplayName = name, PasswordHash = "hash" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    public static async Task<Team> AddTeamAsync(PlanFlowDbContext db, params (User User, TeamRole Role)[] members)
    {
        var team = new Team { Name = "Team" };
        db.Teams.Add(team);
        foreach (var (user, role) in members)
        {
            db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = user.Id, Role = role });
        }
        await db.SaveChangesAsync();
        return team;
    }
}
