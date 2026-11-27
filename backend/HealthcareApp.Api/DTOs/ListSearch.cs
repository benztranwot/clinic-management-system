using Microsoft.EntityFrameworkCore;

namespace HealthcareApp.Api.Dtos;

public sealed record ClinicSearch(string? Name = null, string? Address = null, string? TimeZone = null, int Page = 1);
public sealed record PatientSearch(string? FirstName = null, string? LastName = null, DateOnly? DateOfBirth = null, int Page = 1);
public sealed record ReferralSearch(string? Patient = null, string? Service = null, string? Clinic = null, string? Stage = null, int Page = 1);

public sealed record PageResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public bool HasMore => (long)Page * PageSize < TotalCount;
}

public static class ListPaging
{
    public const int ReferralSize = 10;
    public const int DirectorySize = 16;
    public const int AppointmentSize = 14;

    public static bool IsValid(int page, int pageSize, params string?[] terms) =>
        page >= 1 && page <= int.MaxValue / pageSize && terms.All(term => term is null || term.Length <= 200);

    // The caller supplies an authorized, filtered query with a unique ordering.
    public static async Task<PageResult<T>> ReadAsync<T>(IQueryable<T> query, int page, int pageSize, CancellationToken cancellationToken)
    {
        var count = await query.CountAsync(cancellationToken);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new(items, page, pageSize, count);
    }
}
