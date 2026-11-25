using System.Reflection;

namespace Analyseur.Moteur;

/// <summary>Résultat d'un prédicat.</summary>
/// <param name="Resume">Forme courte pour la liste « Indices trouvés » (ex. « types compatibles (nvarchar(50) → nvarchar(100)) »).</param>
/// <param name="Developpe">Forme longue pour la vue principale.</param>
/// <param name="Raison">Pourquoi ABSENT ou NON_VERIFIABLE.</param>
/// <param name="Valeurs">Valeurs nommées utilisables dans les modèles de message ({predicat.cle}).</param>
/// <param name="Detail">Lignes ajoutées au détail de l'alerte.</param>
public sealed record ResultatPredicat(
    Etat Etat,
    string Resume,
    string? Developpe = null,
    string? Raison = null,
    IReadOnlyDictionary<string, string>? Valeurs = null,
    IReadOnlyList<string>? Detail = null)
{
    public static ResultatPredicat Trouve(string resume, string? developpe = null,
        IReadOnlyDictionary<string, string>? valeurs = null, IReadOnlyList<string>? detail = null)
        => new(Etat.TROUVE, resume, developpe ?? resume, null, valeurs, detail);
    public static ResultatPredicat Absent(string raison, IReadOnlyList<string>? detail = null)
        => new(Etat.ABSENT, raison, null, raison, null, detail);
    public static ResultatPredicat NonVerifiable(string raison, IReadOnlyDictionary<string, string>? valeurs = null)
        => new(Etat.NON_VERIFIABLE, raison, null, raison, valeurs);
}

/// <summary>Prédicat : capacité écrite en C#, versionnée, testée séparément.
/// Les règles JSON ne font que combiner des prédicats.</summary>
public interface IPredicat
{
    string Nom { get; }          // ex. usages_transferes
    string Version { get; }      // ex. 1.0
    string Libelle { get; }      // ex. usages transférés
    string Cible { get; }        // suppression_colonne ou paire_suppression_ajout
    ResultatPredicat Evaluer(object cible, Faits faits);
}

public static class Cibles
{
    public const string Suppression = "suppression_colonne";
    public const string Paire = "paire_suppression_ajout";
}

/// <summary>Registre des prédicats, découverts dans les assemblages Predicats.*.dll
/// du dossier de l'outil. Le moteur ne référence aucun prédicat.</summary>
public sealed class RegistrePredicats
{
    private readonly Dictionary<string, IPredicat> _parNom = new(StringComparer.Ordinal);

    public IReadOnlyCollection<IPredicat> Tous => _parNom.Values;

    public IPredicat? Trouver(string nom) => _parNom.GetValueOrDefault(nom);

    public static RegistrePredicats Charger(string dossier)
    {
        var registre = new RegistrePredicats();
        foreach (var dll in Directory.GetFiles(dossier, "Predicats.*.dll").OrderBy(f => f, StringComparer.Ordinal))
        {
            var assemblage = Assembly.LoadFrom(dll);
            foreach (var type in assemblage.GetTypes().Where(t => typeof(IPredicat).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface))
            {
                var p = (IPredicat)Activator.CreateInstance(type)!;
                registre._parNom[p.Nom] = p;
            }
        }
        return registre;
    }
}
