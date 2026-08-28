using MediatR;
using PlanFlow.Application.Alerts.Common;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;

namespace PlanFlow.Application.Alerts.Commands.CreateAlert;

public class CreateAlertCommandHandler : IRequestHandler<CreateAlertCommand, AlertDto>
{
    private readonly IApplicationDbContext _context;

    public CreateAlertCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<AlertDto> Handle(CreateAlertCommand request, CancellationToken cancellationToken)
    {
        var alert = new Alert
        {
            UserId = request.UserId,
            TaskItemId = request.TaskItemId,
            Type = request.Type,
            Message = request.Message
        };

        _context.Alerts.Add(alert);
        await _context.SaveChangesAsync(cancellationToken);

        return AlertDto.FromEntity(alert);
    }
}
