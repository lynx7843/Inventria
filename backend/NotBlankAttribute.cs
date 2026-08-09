using System.ComponentModel.DataAnnotations;

namespace Inventria;

/// <summary>
/// Requires a string that holds something a person could read.
///
/// <c>[Required]</c> alone is not that test: it rejects null and "" but accepts
/// " ", which reaches the database as a name nobody can see, a SKU nobody can
/// scan, and - once trimmed for comparison - a value that collides with every
/// other blank one while looking distinct to a unique index.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class NotBlankAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) =>
        value is string text && !string.IsNullOrWhiteSpace(text);
}
