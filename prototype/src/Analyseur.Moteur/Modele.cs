namespace Analyseur.Moteur;

/// <summary>État d'un indice. NON_VERIFIABLE n'est jamais compté comme TROUVE.</summary>
public enum Etat { TROUVE, ABSENT, NON_VERIFIABLE }

/// <summary>Opération lue dans un fichier de migration EF Core (fait syntaxique).</summary>
public sealed record OperationMigration(
    string Migration,        // nom du fichier sans extension, ex. 20261125093000_ClientNomComplet
    string Fichier,          // chemin relatif au projet, ex. Migrations/20261125093000_ClientNomComplet.cs
    string Methode,          // Up ou Down
    int Ligne,               // ligne de début de l'appel (base 1)
    string Type,             // DropColumn, AddColumn, RenameColumn, Sql
    string? Table,
    string? Colonne,
    string? TypeColonne,     // ex. nvarchar(100)
    bool? Nullable,
    string? Sql,             // texte SQL quand il est constant
    bool SqlConstant);

/// <summary>Propriété lue dans le ModelSnapshot d'une révision.</summary>
public sealed record ProprieteModele(
    string Entite,           // nom complet, ex. ClientsDemo.Models.Client
    string Propriete,
    string Table,
    string Colonne,
    string? TypeColonne,
    bool? Nullable)
{
    public string EntiteCourte => Entite.Contains('.') ? Entite[(Entite.LastIndexOf('.') + 1)..] : Entite;
}

/// <summary>Ce que l'outil sait de la révision N-1.</summary>
public sealed record RevisionPrecedente(
    string? Sha,                 // court (7 caractères)
    string? Racine,              // dossier de travail préparé
    bool FichiersDisponibles,    // les sources de N-1 sont lisibles
    string? RaisonFichiers,      // pourquoi elles ne le sont pas
    bool Chargeable,             // la compilation de N-1 est possible (paquets restaurés)
    string? RaisonChargement)
{
    public static RevisionPrecedente Indisponible(string raison) => new(null, null, false, raison, false, raison);
}

/// <summary>Cible d'une règle : une suppression de colonne.</summary>
public sealed record CibleSuppression(OperationMigration Suppression);

/// <summary>Cible d'une règle : une suppression et un ajout de colonne.</summary>
public sealed record CiblePaire(OperationMigration Suppression, OperationMigration Ajout);

/// <summary>Faits disponibles pour les prédicats. Les modules ne s'appellent pas entre eux.</summary>
public sealed class Faits
{
    public required string RacineN { get; init; }
    public required string ShaN { get; init; }
    public required RevisionPrecedente Precedente { get; init; }
    public required IReadOnlyList<OperationMigration> Operations { get; init; }       // migrations nouvelles de N
    public required IReadOnlyList<OperationMigration> ToutesOperationsN { get; init; } // y compris Down
    public required IReadOnlyList<ProprieteModele>? ModeleN { get; init; }
    public required IReadOnlyList<ProprieteModele>? ModelePrecedent { get; init; }
    public required AnalyseurUsages Usages { get; init; }
}
