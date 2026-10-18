using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;

namespace PlanFlow.Application.ItemLists;

/// <summary>Renames a list. Its creator may, and so may anybody who manages the team's tasks (<paramref name="CanManageAny"/>: Owner/Admin).</summary>
public record RenameItemListCommand(Guid TeamId, Guid ListId, string Name, Guid CallerUserId, bool CanManageAny) : IRequest<ItemListDto>;

public class RenameItemListCommandValidator : AbstractValidator<RenameItemListCommand>
{
    public RenameItemListCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(ItemList.MaxNameLength);
    }
}

public class RenameItemListCommandHandler : IRequestHandler<RenameItemListCommand, ItemListDto>
{
    private readonly IApplicationDbContext _context;

    public RenameItemListCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ItemListDto> Handle(RenameItemListCommand request, CancellationToken cancellationToken)
    {
        var list = await _context.ItemLists.FirstOrDefaultAsync(l => l.Id == request.ListId && l.TeamId == request.TeamId, cancellationToken)
            ?? throw new NotFoundException(nameof(ItemList), request.ListId);

        if (!request.CanManageAny && list.CreatedByUserId != request.CallerUserId)
        {
            throw new ForbiddenAccessException("Only the person who made the list, or a team owner or admin, can change it.");
        }

        var name = request.Name.Trim();
        await ItemListNames.EnsureFreeAsync(_context, list.TeamId, list.Kind, name, list.Id, cancellationToken);

        list.Name = name;
        list.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return ItemListDto.FromEntity(list);
    }
}
