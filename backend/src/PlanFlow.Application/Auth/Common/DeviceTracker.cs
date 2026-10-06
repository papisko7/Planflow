using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;

namespace PlanFlow.Application.Auth.Common;

/// <summary>Remembers which devices an account has signed in from, so a sign-in from an unfamiliar one can be flagged.</summary>
internal static class DeviceTracker
{
    public const string UnknownDeviceName = "Unknown device";

    /// <summary>
    /// Records the sign-in. Returns true only when the device is new AND the account already had other known devices —
    /// an account with no device history yet (just registered, or created before this feature) would otherwise
    /// flag its very first login as suspicious.
    /// </summary>
    public static async Task<bool> RecordSignInAsync(
        IApplicationDbContext context, Guid userId, string? deviceId, string? deviceName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return false;
        }

        var id = deviceId.Trim();
        var name = string.IsNullOrWhiteSpace(deviceName) ? UnknownDeviceName : deviceName.Trim();
        var nowUtc = DateTime.UtcNow;

        var devices = await context.UserDevices.Where(d => d.UserId == userId).ToListAsync(cancellationToken);
        var known = devices.FirstOrDefault(d => d.DeviceId == id);
        if (known is not null)
        {
            known.LastSeenAtUtc = nowUtc;
            known.Name = name;
            return false;
        }

        context.UserDevices.Add(new UserDevice { UserId = userId, DeviceId = id, Name = name, LastSeenAtUtc = nowUtc });
        return devices.Count > 0;
    }
}
