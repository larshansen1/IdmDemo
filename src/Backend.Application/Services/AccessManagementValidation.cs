using Backend.Domain.Exceptions;
using Backend.Domain.Repositories;

namespace Backend.Application.Services;

internal static class AccessManagementValidation
{
    public static string ValidateValue(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException($"{fieldName} is required and must not be empty or whitespace.");
        }

        if (value.Length > 128)
        {
            throw new ValidationException($"{fieldName} must not exceed 128 characters.");
        }

        if (value.Any(char.IsWhiteSpace))
        {
            throw new ValidationException($"{fieldName} must not contain whitespace.");
        }

        if (value.Contains('\0', StringComparison.Ordinal))
        {
            throw new ValidationException($"{fieldName} must not contain null characters.");
        }

        return value;
    }

    public static List<string> ValidateNames(IReadOnlyList<string> values, string fieldName)
    {
        ArgumentNullException.ThrowIfNull(values);

        var normalized = new List<string>();
        foreach (var value in values)
        {
            normalized.Add(ValidateValue(value, $"{fieldName} values"));
        }

        return normalized.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
    }

    public static async Task ValidateActiveRolesAsync(
        IGlobalRoleRepository roleRepository,
        IReadOnlyList<string> roleValues,
        string fieldName,
        CancellationToken cancellationToken)
    {
        foreach (var roleValue in roleValues)
        {
            if (!await roleRepository.ExistsActiveByValueAsync(roleValue, cancellationToken).ConfigureAwait(false))
            {
                throw new ValidationException($"{fieldName} contains an unknown or inactive role '{roleValue}'.");
            }
        }
    }

    public static async Task ValidateActiveScopesAsync(
        IGlobalScopeRepository scopeRepository,
        IReadOnlyList<string> scopeValues,
        string fieldName,
        CancellationToken cancellationToken)
    {
        foreach (var scopeValue in scopeValues)
        {
            if (!await scopeRepository.ExistsActiveByValueAsync(scopeValue, cancellationToken).ConfigureAwait(false))
            {
                throw new ValidationException($"{fieldName} contains an unknown or inactive scope '{scopeValue}'.");
            }
        }
    }
}
