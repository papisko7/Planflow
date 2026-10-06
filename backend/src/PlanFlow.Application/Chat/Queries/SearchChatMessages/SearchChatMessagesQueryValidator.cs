using FluentValidation;

namespace PlanFlow.Application.Chat.Queries.SearchChatMessages;

public class SearchChatMessagesQueryValidator : AbstractValidator<SearchChatMessagesQuery>
{
    public SearchChatMessagesQueryValidator()
    {
        RuleFor(x => x.TeamId).NotEmpty();
        // Empty is allowed on purpose: an empty term matches every message and is how the client loads history.
        RuleFor(x => x.SearchTerm).NotNull().MaximumLength(256);
    }
}
