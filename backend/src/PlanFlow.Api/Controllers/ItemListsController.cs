using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlanFlow.Api.Common;
using PlanFlow.Api.Contracts;
using PlanFlow.Application.ItemLists;
using PlanFlow.Domain.Authorization;

namespace PlanFlow.Api.Controllers;

/// <summary>
/// The team's own lists of tasks and of meetings. Any member sees and creates lists; renaming or deleting one is for the
/// person who made it and for Owner/Admin (decided in the handler). Deleting a list never deletes the items in it.
/// </summary>
[ApiController]
[Authorize]
[Route("api/teams/{teamId:guid}/lists")]
public class ItemListsController : ControllerBase
{
    private readonly ISender _sender;

    public ItemListsController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet]
    [Authorize(Policy = nameof(Permission.ViewTasks))]
    public async Task<IActionResult> GetLists(Guid teamId, CancellationToken cancellationToken)
    {
        if (User.GetTeamId() != teamId)
        {
            return Forbid();
        }

        return Ok(await _sender.Send(new GetItemListsQuery(teamId), cancellationToken));
    }

    [HttpPost]
    [Authorize(Policy = nameof(Permission.CreateTask))]
    public async Task<IActionResult> CreateList(Guid teamId, CreateItemListRequest request, CancellationToken cancellationToken)
    {
        if (User.GetTeamId() != teamId)
        {
            return Forbid();
        }

        var list = await _sender.Send(new CreateItemListCommand(teamId, request.Name, request.Kind, User.GetUserId()), cancellationToken);
        return Created($"api/teams/{teamId}/lists/{list.Id}", list);
    }

    [HttpPut("{listId:guid}")]
    [Authorize(Policy = nameof(Permission.EditOwnTask))]
    public async Task<IActionResult> RenameList(Guid teamId, Guid listId, RenameItemListRequest request, CancellationToken cancellationToken)
    {
        if (User.GetTeamId() != teamId)
        {
            return Forbid();
        }

        var list = await _sender.Send(
            new RenameItemListCommand(teamId, listId, request.Name, User.GetUserId(), User.HasPermission(Permission.EditAnyTask)), cancellationToken);
        return Ok(list);
    }

    [HttpDelete("{listId:guid}")]
    [Authorize(Policy = nameof(Permission.EditOwnTask))]
    public async Task<IActionResult> DeleteList(Guid teamId, Guid listId, CancellationToken cancellationToken)
    {
        if (User.GetTeamId() != teamId)
        {
            return Forbid();
        }

        await _sender.Send(
            new DeleteItemListCommand(teamId, listId, User.GetUserId(), User.HasPermission(Permission.EditAnyTask)), cancellationToken);
        return NoContent();
    }
}
