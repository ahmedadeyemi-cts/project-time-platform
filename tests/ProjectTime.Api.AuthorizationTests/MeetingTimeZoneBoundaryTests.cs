using ProjectTime.Api.Modules;

internal static class MeetingTimeZoneBoundaryTests
{
    internal static async Task RunAsync()
    {
        int checks = 0;
        foreach (var zone in new[] { "/usr/share/zoneinfo/Etc/UTC", "/etc/passwd", "/dev/zero", "../Etc/UTC", "UTC" })
        {
            var request = new FlowHiveMeetingDraftRequest("Example", "", "", null, null, zone, []);
            try
            {
                await ProjectFlowHiveCollaborationStore.CreateMeetingAsync(null!, null!, Guid.Empty, Guid.Empty,
                    request, CancellationToken.None);
                throw new Exception("Invalid meeting accepted");
            }
            catch (ProjectFlowHiveCollaborationStore.InputException exception)
            {
                // UTC must reach the next validation; path-like inputs must stop before any database work.
                if (exception.Field != (zone == "UTC" ? "endsAt" : "timezoneName"))
                    throw new Exception($"Meeting time-zone validation for {zone} returned {exception.Field}");
                checks++;
            }
        }
        Console.WriteLine($"MEETING_TIME_ZONE_BOUNDARY=PASS assertions={checks}");
    }
}
