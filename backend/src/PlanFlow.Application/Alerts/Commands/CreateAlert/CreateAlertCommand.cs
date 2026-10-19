using MediatR;
using PlanFlow.Application.Alerts.Common;

namespace PlanFlow.Application.Alerts.Commands.CreateAlert;

/// <summary>Creates a user-facing notification, typically raised by AlertingJob.</summary>
public record CreateAlertCommand(
    Guid UserId,
    Guid? CalendarItemId,
    Domain.Enums.AlertType Type,
    string Message) : IRequest<AlertDto>;
