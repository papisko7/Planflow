using FluentValidation;

namespace PlanFlow.Application.Chat.Queries.SearchChatMessages;

public class SearchChatMessagesQueryValidator : AbstractValidator<SearchChatMessagesQuery>
{
    public SearchChatMessagesQueryValidator()
    {
        RuleFor(x => x.TeamId).NotEmpty();
        RuleFor(x => x.SearchTerm).NotEmpty().MaximumLength(256);
    }
}
