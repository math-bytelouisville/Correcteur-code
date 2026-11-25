using System.ComponentModel.DataAnnotations;

namespace ClientsDemo.Models;

/// <summary>Client fictif de démonstration.</summary>
public class Client
{
    public int Id { get; set; }

    [MaxLength(100)]
    public string Nom { get; set; } = "";

    public DateTime? DateInscription { get; set; }

    [MaxLength(50)]
    public string? Ville { get; set; }
}
