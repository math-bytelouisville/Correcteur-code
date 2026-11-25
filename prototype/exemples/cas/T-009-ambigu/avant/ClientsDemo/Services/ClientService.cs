using ClientsDemo.Data;
using ClientsDemo.Models;

namespace ClientsDemo.Services;

/// <summary>Règles d'enregistrement d'un client (projet fictif).</summary>
public class ClientService
{
    private readonly ClientsDemoContext _contexte;
    private readonly Journal _journal;

    public ClientService(ClientsDemoContext contexte, Journal journal)
    {
        _contexte = contexte;
        _journal = journal;
    }

    /// <summary>Valide puis enregistre le client.</summary>
    public async Task Enregistrer(Client client)
    {
        if (client.Id <= 0)
        {
            throw new ArgumentException("Identifiant invalide.", nameof(client));
        }

        // Contrôle de longueur avant l'écriture en base :
        // la colonne est limitée à 100 caractères
        // et une saisie plus longue est refusée.

        if (client.Nom.Length > 100) throw new ArgumentException("Nom trop long.", nameof(client));
        var ville = client.Ville;

        _journal.Ecrire($"Enregistrement du client {client.Id}");
        await _contexte.SaveChangesAsync();
    }
}
