using Attener.Features.Subjects;

namespace Attener.Features.Timetable;

public sealed record WeeklySessionSummary(
    long Id,
    long SubjectId,
    string SubjectName,
    SubjectType Type,
    int DayOfWeek,
    string StartTime,
    string EndTime,
    string EffectiveFrom,
    string? EffectiveTo,
    string Venue = "",
    string Professor = "")
{
    public bool HasProfessor => !string.IsNullOrWhiteSpace(Professor);
    public bool HasVenue => !string.IsNullOrWhiteSpace(Venue);
}

