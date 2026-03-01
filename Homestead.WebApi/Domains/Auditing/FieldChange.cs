using System.Text.Json.Serialization;

namespace Homestead.WebApi.Domains.Auditing;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ScalarFieldChange), "scalar")]
[JsonDerivedType(typeof(ArrayFieldChange), "array")]
public abstract record FieldChange(string Field);

public record ScalarFieldChange(string Field, string? OldValue, string? NewValue) : FieldChange(Field);

public record ArrayFieldChange(string Field, List<string>? OldValue, List<string>? NewValue) : FieldChange(Field);
