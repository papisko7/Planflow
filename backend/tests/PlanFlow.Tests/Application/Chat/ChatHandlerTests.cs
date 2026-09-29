using PlanFlow.Application.Chat.Commands.SendChatMessage;
using PlanFlow.Application.Chat.Queries.SearchChatMessages;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Tests.Application.Chat;

public class ChatHandlerTests
{
    [Fact]
    public async Task Send_PersistsMessageAndReturnsSenderName()
    {
        var db = TestDb.Create();
        var ann = await TestDb.AddUserAsync(db, "Ann");
        var team = await TestDb.AddTeamAsync(db, (ann, TeamRole.Member));

        var dto = await new SendChatMessageCommandHandler(db)
            .Handle(new SendChatMessageCommand(team.Id, ann.Id, "hello"), CancellationToken.None);

        Assert.Equal("Ann", dto.SenderDisplayName);
        Assert.Single(db.ChatMessages);
    }

    [Fact]
    public async Task Send_UnknownSender_ThrowsNotFound()
    {
        var db = TestDb.Create();
        var team = await TestDb.AddTeamAsync(db);

        await Assert.ThrowsAsync<NotFoundException>(
            () => new SendChatMessageCommandHandler(db)
                .Handle(new SendChatMessageCommand(team.Id, Guid.NewGuid(), "hi"), CancellationToken.None));
    }

    [Fact]
    public async Task Search_IsCaseInsensitiveAndTeamScoped()
    {
        var db = TestDb.Create();
        var ann = await TestDb.AddUserAsync(db, "Ann");
        var teamA = await TestDb.AddTeamAsync(db, (ann, TeamRole.Member));
        var teamB = await TestDb.AddTeamAsync(db, (ann, TeamRole.Member));
        var send = new SendChatMessageCommandHandler(db);
        await send.Handle(new SendChatMessageCommand(teamA.Id, ann.Id, "Deploy tonight"), CancellationToken.None);
        await send.Handle(new SendChatMessageCommand(teamA.Id, ann.Id, "lunch?"), CancellationToken.None);
        await send.Handle(new SendChatMessageCommand(teamB.Id, ann.Id, "deploy elsewhere"), CancellationToken.None);

        var found = await new SearchChatMessagesQueryHandler(db)
            .Handle(new SearchChatMessagesQuery(teamA.Id, "DEPLOY"), CancellationToken.None);

        var single = Assert.Single(found);
        Assert.Equal("Deploy tonight", single.Content);
    }
}
