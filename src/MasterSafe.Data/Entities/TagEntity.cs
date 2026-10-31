// File:    TagEntity.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.ComponentModel.DataAnnotations;

namespace MasterSafe.Data.Entities;

/// <summary>
/// A tag that can be associated with many accounts.
/// </summary>
public sealed class TagEntity
{
    [Key]
    public int Id { get; set; }

    [MaxLength(64)]
    public string Name { get; set; } = string.Empty;

    public ICollection<AccountEntity> Accounts { get; set; } = [];
}
