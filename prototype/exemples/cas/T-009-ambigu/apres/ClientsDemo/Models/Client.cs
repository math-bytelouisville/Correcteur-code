using System.ComponentModel.DataAnnotations;

namespace ClientsDemo.Models;

/// <summary>Client fictif de démonstration.</summary>
public class Client
{
    public int Id { get; set; }

    [MaxLength(100)]
    public string Nom { get; set; } = "";

    [MaxLength(20)]
    public string? Fax { get; set; }

    [MaxLength(100)]
    public string? Commentaire { get; set; }
}
