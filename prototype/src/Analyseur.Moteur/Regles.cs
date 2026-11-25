using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Analyseur.Moteur;

/// <summary>Règle déclarative. Aucun champ n'accepte d'expression libre : seulement des noms de
/// prédicats connus et des textes avec des marqueurs {table}, {colonne}, {ancienne}, {nouvelle}
/// ou {predicat.cle}.</summary>
public sealed class Regle
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("forme")] public string Forme { get; set; } = "json";
    [JsonPropertyName("categorie")] public string Categorie { get; set; } = "";
    [JsonPropertyName("niveau_epistemique")] public string Niveau { get; set; } = "";   // CONSTAT ou HYPOTHESE
    [JsonPropertyName("cible")] public string Cible { get; set; } = "";
    [JsonPropertyName("gravite")] public string? Gravite { get; set; }
    [JsonPropertyName("auteur")] public string? Auteur { get; set; }
    [JsonPropertyName("predicats_requis")] public Dictionary<string, string> PredicatsRequis { get; set; } = new();

    // Constat
    [JsonPropertyName("message")] public MessageRegle? Message { get; set; }

    // Hypothèse
    [JsonPropertyName("rattachement")] public string? Rattachement { get; set; }
    [JsonPropertyName("conditions_obligatoires")] public List<string>? ConditionsObligatoires { get; set; }
    [JsonPropertyName("affichage_principal_si")] public List<string>? AffichagePrincipalSi { get; set; }
    [JsonPropertyName("indices_complementaires")] public List<string>? IndicesComplementaires { get; set; }
    [JsonPropertyName("intitule_detail")] public string? IntituleDetail { get; set; }
    [JsonPropertyName("question_principale")] public string? QuestionPrincipale { get; set; }
    [JsonPropertyName("inconnu")] public string? Inconnu { get; set; }
    [JsonPropertyName("correction")] public string? Correction { get; set; }

    [JsonIgnore] public string Fichier { get; set; } = "";
    [JsonIgnore] public string EtatCycle { get; set; } = "";
    [JsonIgnore] public bool Active => EtatCycle.StartsWith("ACTIVE", StringComparison.Ordinal) || EtatCycle.StartsWith("CORRECTION", StringComparison.Ordinal);

    public IEnumerable<string> PredicatsUtilises()
    {
        IEnumerable<string> v = Enumerable.Empty<string>();
        if (ConditionsObligatoires is not null) v = v.Concat(ConditionsObligatoires);
        if (AffichagePrincipalSi is not null) v = v.Concat(AffichagePrincipalSi);
        if (IndicesComplementaires is not null) v = v.Concat(IndicesComplementaires);
        if (Message?.Selon is not null) v = v.Concat(Message.Selon.Keys);
        return v.Distinct(StringComparer.Ordinal);
    }

    public IEnumerable<string> Textes()
    {
        if (Message is not null)
        {
            yield return Message.Base;
            foreach (var parEtat in Message.Selon.Values) foreach (var t in parEtat.Values) yield return t;
        }
        foreach (var t in new[] { IntituleDetail, QuestionPrincipale, Inconnu, Correction }) if (t is not null) yield return t;
    }
}

public sealed class MessageRegle
{
    [JsonPropertyName("base")] public string Base { get; set; } = "";
    [JsonPropertyName("selon")] public Dictionary<string, Dictionary<string, string>> Selon { get; set; } = new();
}

public sealed class EntreeVerrou
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("fichier")] public string Fichier { get; set; } = "";
    [JsonPropertyName("etat")] public string Etat { get; set; } = "";
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
}

public sealed class Verrou
{
    [JsonPropertyName("regles")] public List<EntreeVerrou> Regles { get; set; } = new();
}

public sealed class ReglesRefusees : Exception
{
    public ReglesRefusees(string message) : base(message) { }
}

public static class ChargeurRegles
{
    public const string NomVerrou = "regles.lock.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Disallow,
    };

    public static readonly JsonSerializerOptions OptionsEcriture = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly Regex Marqueur = new(@"\{([^{}]*)\}", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> MarqueursSimples = new(StringComparer.Ordinal) { "table", "colonne", "ancienne", "nouvelle" };
    private static readonly HashSet<string> Niveaux = new(StringComparer.Ordinal) { "CONSTAT", "HYPOTHESE" };
    private static readonly HashSet<string> EtatsPredicat = new(StringComparer.Ordinal) { "TROUVE", "ABSENT", "NON_VERIFIABLE" };

    public static string Empreinte(string fichier)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fichier))).ToLowerInvariant();

    /// <summary>Lit et valide une règle. Lève ReglesRefusees avec le message affiché à l'utilisateur.</summary>
    public static Regle LireEtValider(string fichier, RegistrePredicats registre)
    {
        Regle? regle;
        try
        {
            regle = JsonSerializer.Deserialize<Regle>(File.ReadAllText(fichier), Options);
        }
        catch (JsonException e)
        {
            throw new ReglesRefusees($"Règle {Path.GetFileNameWithoutExtension(fichier)} refusée : JSON invalide ou champ non autorisé ({e.Message.Split('.')[0]}).");
        }
        if (regle is null || string.IsNullOrWhiteSpace(regle.Id))
            throw new ReglesRefusees($"Règle {Path.GetFileNameWithoutExtension(fichier)} refusée : identifiant manquant.");
        regle.Fichier = Path.GetFileName(fichier);
        var id = regle.Id;

        if (regle.Forme != "json")
            throw new ReglesRefusees($"Règle {id} refusée : forme {regle.Forme} non prise en charge par ce chargeur.");
        if (!Niveaux.Contains(regle.Niveau))
            throw new ReglesRefusees($"Règle {id} refusée : niveau épistémique {regle.Niveau} inconnu.");
        if (regle.Cible != Cibles.Suppression && regle.Cible != Cibles.Paire)
            throw new ReglesRefusees($"Règle {id} refusée : cible {regle.Cible} inconnue.");

        foreach (var nom in regle.PredicatsUtilises().Concat(regle.PredicatsRequis.Keys).Distinct(StringComparer.Ordinal))
        {
            var p = registre.Trouver(nom)
                ?? throw new ReglesRefusees($"Règle {id} refusée : prédicat inconnu {nom}.");
            if (!regle.PredicatsRequis.TryGetValue(nom, out var versionExigee))
                throw new ReglesRefusees($"Règle {id} refusée : prédicat {nom} utilisé sans version exigée dans predicats_requis.");
            if (versionExigee != p.Version)
                throw new ReglesRefusees($"Règle {id} refusée : prédicat {nom} disponible en version {p.Version}, version {versionExigee} exigée.");
            if (p.Cible != regle.Cible)
                throw new ReglesRefusees($"Règle {id} refusée : prédicat {nom} s'applique à {p.Cible}, la règle cible {regle.Cible}.");
        }

        if (regle.Message is not null)
            foreach (var (nom, parEtat) in regle.Message.Selon)
                foreach (var etat in parEtat.Keys)
                    if (!EtatsPredicat.Contains(etat))
                        throw new ReglesRefusees($"Règle {id} refusée : état {etat} inconnu pour {nom}.");

        foreach (var texte in regle.Textes())
            foreach (Match m in Marqueur.Matches(texte))
            {
                var nom = m.Groups[1].Value;
                var point = nom.IndexOf('.');
                var valide = point < 0
                    ? MarqueursSimples.Contains(nom)
                    : regle.PredicatsRequis.ContainsKey(nom[..point]) && Regex.IsMatch(nom[(point + 1)..], "^[a-z_]+$");
                if (!valide)
                    throw new ReglesRefusees($"Règle {id} refusée : marqueur {{{nom}}} non autorisé.");
            }

        if (regle.Niveau == "CONSTAT" && (regle.Message is null || regle.Gravite is null))
            throw new ReglesRefusees($"Règle {id} refusée : un constat exige un message et une gravité.");
        if (regle.Niveau == "HYPOTHESE" && (regle.ConditionsObligatoires is null || regle.IntituleDetail is null || regle.QuestionPrincipale is null))
            throw new ReglesRefusees($"Règle {id} refusée : une hypothèse exige conditions_obligatoires, intitule_detail et question_principale.");
        return regle;
    }

    public static Verrou LireVerrou(string dossier)
    {
        var chemin = Path.Combine(dossier, NomVerrou);
        return JsonSerializer.Deserialize<Verrou>(File.ReadAllText(chemin), Options) ?? new Verrou();
    }

    public static void EcrireVerrou(string dossier, Verrou verrou)
        => File.WriteAllText(Path.Combine(dossier, NomVerrou), JsonSerializer.Serialize(verrou, OptionsEcriture) + "\n");

    /// <summary>Charge les règles listées dans le verrou, en vérifiant leur empreinte.</summary>
    public static List<Regle> ChargerDepuisVerrou(string dossier, RegistrePredicats registre, List<string> avertissements)
    {
        var regles = new List<Regle>();
        foreach (var e in LireVerrou(dossier).Regles)
        {
            var chemin = Path.Combine(dossier, e.Fichier);
            if (!File.Exists(chemin)) { avertissements.Add($"Règle {e.Id} ignorée : fichier {e.Fichier} introuvable."); continue; }
            if (Empreinte(chemin) != e.Sha256) { avertissements.Add($"Règle {e.Id} ignorée : empreinte différente de celle du verrou."); continue; }
            var r = LireEtValider(chemin, registre);
            if (r.Version != e.Version) { avertissements.Add($"Règle {e.Id} ignorée : version {r.Version} différente du verrou ({e.Version})."); continue; }
            r.EtatCycle = e.Etat;
            regles.Add(r);
        }
        return regles;
    }
}
