using System.ComponentModel.DataAnnotations;

namespace ClientsDemo.Models;

/// <summary>Client fictif de démonstration.</summary>
public class Client
{
    public int Id { get; set; }

    [MaxLength(100)]
    public string NomComplet { get; set; } = "";

    [MaxLength(20)]
    public string? Fax { get; set; }

    [MaxLength(50)]
    public string? Ville { get; set; }
}
