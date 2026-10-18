using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Application.ItemLists;

/// <summary>One of the team's own lists. <see cref="CreatedByUserId"/> is who may rename / delete it besides Owner/Admin.</summary>
public record ItemListDto(Guid Id, Guid TeamId, string Name, ItemListKind Kind, Guid? CreatedByUserId)
{
    public static ItemListDto FromEntity(ItemList list) => new(list.Id, list.TeamId, list.Name, list.Kind, list.CreatedByUserId);
}
