namespace HrTool_API.Domain.Entities;

/// <summary>Entities, deren <see cref="UpdatedAt"/> der DbContext beim Speichern einer Änderung setzt.</summary>
public interface IHasUpdatedAt
{
    DateTime UpdatedAt { get; set; }
}
