using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Analyseur.Moteur;

/// <summary>Résultat de la préparation d'une révision, écrit par « analyseur preparer ».</summary>
public sealed class EtatPreparation
{
    [JsonPropertyName("sha")] public string Sha { get; set; } = "";
    [JsonPropertyName("restauration")] public string Restauration { get; set; } = ""; // ok, echec, echec_hors_ligne
    [JsonPropertyName("hors_ligne")] public bool HorsLigne { get; set; }
    [JsonPropertyName("erreur")] public string? Erreur { get; set; }
}

public static class EtatsPreparation
{
    public static string Dossier(string racine) => Path.Combine(racine, ".analyseur");
    public static string DossierRevision(string racine, string sha) => Path.Combine(Dossier(racine), "revisions", sha);
    private static string Fichier(string racine, string cle) => Path.Combine(Dossier(racine), "etat", cle + ".json");

    public static EtatPreparation? Lire(string racine, string cle)
    {
        var f = Fichier(racine, cle);
        return File.Exists(f) ? JsonSerializer.Deserialize<EtatPreparation>(File.ReadAllText(f)) : null;
    }

    public static void Ecrire(string racine, string cle, EtatPreparation etat)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Fichier(racine, cle))!);
        File.WriteAllText(Fichier(racine, cle), JsonSerializer.Serialize(etat, ChargeurRegles.OptionsEcriture));
    }

    /// <summary>Null si les projets de la révision ont leurs paquets restaurés, sinon la raison.</summary>
    public static string? RaisonNonChargeable(string racine, string sha, EtatPreparation? etat)
    {
        if (etat is { Restauration: "echec_hors_ligne" }) return $"restauration des paquets impossible hors ligne pour {sha}";
        if (etat is { Restauration: "echec" }) return $"restauration des paquets impossible pour {sha}";
        foreach (var projet in Directory.GetFiles(racine, "*.csproj", SearchOption.AllDirectories))
        {
            if (projet.Contains(Path.DirectorySeparatorChar + ".analyseur" + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;
            if (!File.Exists(Path.Combine(Path.GetDirectoryName(projet)!, "obj", "project.assets.json")))
                return $"paquets non restaurés pour {sha} (lancez analyseur preparer)";
        }
        return null;
    }
}

public static class Git
{
    public static (int Code, string Sortie) Executer(string dossier, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = dossier, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var sortie = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, sortie.Trim());
    }

    public static string? ShaCourt(string dossier, string reference = "HEAD")
    {
        var (code, sortie) = Executer(dossier, "rev-parse", "--short=7", "--verify", "--quiet", reference + "^{commit}");
        return code == 0 ? sortie : null;
    }
}

/// <summary>Résout la révision N-1 demandée, sans jamais restaurer ni télécharger.</summary>
public static class Revisions
{
    public static RevisionPrecedente Resoudre(string racineN, string? demande)
    {
        if (demande is null) return RevisionPrecedente.Indisponible("révision antérieure indisponible");
        var sha = Git.ShaCourt(racineN, demande);
        if (sha is null) return RevisionPrecedente.Indisponible($"révision {demande} introuvable dans le dépôt");
        var dossier = DossierRevision(racineN, sha);
        if (!Directory.Exists(dossier))
            return new(sha, null, false, $"révision {sha} non préparée (lancez analyseur preparer --revision {sha})", false,
                $"révision {sha} non préparée (lancez analyseur preparer --revision {sha})");
        var raison = EtatsPreparation.RaisonNonChargeable(dossier, sha, EtatsPreparation.Lire(racineN, sha));
        return new(sha, dossier, true, null, raison is null, raison);
    }

    private static string DossierRevision(string racine, string sha) => EtatsPreparation.DossierRevision(racine, sha);
}
