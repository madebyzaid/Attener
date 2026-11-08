namespace Attener.Features.Timetable;

public sealed record WeeklySessionSummary(
    long Id,
    long SubjectId,
    string SubjectName,
    int DayOfWeek,
    string StartTime,
    string EndTime,
    string EffectiveFrom,
    string? EffectiveTo);

