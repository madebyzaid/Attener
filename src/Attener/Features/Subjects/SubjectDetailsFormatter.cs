namespace Attener.Features.Subjects;

public static class SubjectDetailsFormatter
{
    public static string Format(string? venue, string? professor) =>
        string.Join(" · ", new[] { venue, professor }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim()));
}
