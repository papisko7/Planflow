using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Application.ItemLists;

/// <summary>Adds a list of the team's own. Any member may; a name is unique among the lists of the same kind in the team.</summary>
public record CreateItemListCommand(Guid TeamId, string Name, ItemListKind Kind, Guid CreatedByUserId) : IRequest<ItemListDto>;

public class CreateItemListCommandValidator : AbstractValidator<CreateItemListCommand>
{
    public CreateItemListCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(ItemList.MaxNameLength);
        RuleFor(x => x.Kind).IsInEnum();
    }
}

public class CreateItemListCommandHandler : IRequestHandler<CreateItemListCommand, ItemListDto>
{
    private readonly IApplicationDbContext _context;

    public CreateItemListCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ItemListDto> Handle(CreateItemListCommand request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        await ItemListNames.EnsureFreeAsync(_context, request.TeamId, request.Kind, name, null, cancellationToken);

        var list = new ItemList { TeamId = request.TeamId, Name = name, Kind = request.Kind, CreatedByUserId = request.CreatedByUserId };
        _context.ItemLists.Add(list);
        await _context.SaveChangesAsync(cancellationToken);
        return ItemListDto.FromEntity(list);
    }
}

internal static class ItemListNames
{
    /// <summary>Two task lists (or two meeting lists) of one team may not share a name, whatever the letter case.</summary>
    public static async Task EnsureFreeAsync(
        IApplicationDbContext context, Guid teamId, ItemListKind kind, string name, Guid? exceptListId, CancellationToken cancellationToken)
    {
        var lower = name.ToLower();
        var taken = await context.ItemLists.AsNoTracking()
            .AnyAsync(l => l.TeamId == teamId && l.Kind == kind && l.Id != exceptListId && l.Name.ToLower() == lower, cancellationToken);
        if (taken)
        {
            throw new ConflictException($"A list named \"{name}\" already exists.");
        }
    }
}
