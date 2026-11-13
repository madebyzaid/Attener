namespace Attener.Features.Subjects;

public enum SubjectType
{
    Lecture,
    Tutorial,
    Practical
}

public sealed record SubjectClassDetails(SubjectType Type, string Professor, string Venue)
{
    public bool HasProfessor => !string.IsNullOrWhiteSpace(Professor);
    public bool HasVenue => !string.IsNullOrWhiteSpace(Venue);
}

public sealed record SubjectSummary(
    long Id,
    string Name,
    IReadOnlyList<SubjectClassDetails> ClassDetails,
    bool IsArchived)
{
    public IReadOnlyList<SubjectType> Types => ClassDetails.Select(details => details.Type).ToArray();

    public SubjectClassDetails DetailsFor(SubjectType type) =>
        ClassDetails.First(details => details.Type == type);
}

