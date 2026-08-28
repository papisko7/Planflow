using MediatR;
using PlanFlow.Application.Alerts.Common;

namespace PlanFlow.Application.Alerts.Commands.CreateAlert;

/// <summary>Creates a user-facing notification, typically raised by AlertingJob (Phase 1.5).</summary>
public record CreateAlertCommand(
    Guid UserId,
    Guid? TaskItemId,
    Domain.Enums.AlertType Type,
    string Message) : IRequest<AlertDto>;
