using System.Text.RegularExpressions;
using Analyseur.Moteur;

namespace Predicats.Base;

public sealed class MemeTable : IPredicat
{
    public string Nom => "meme_table";
    public string Version => "1.0";
    public string Libelle => "même table";
    public string Cible => Cibles.Paire;

    public ResultatPredicat Evaluer(object cible, Faits faits)
    {
        var c = (CiblePaire)cible;
        return string.Equals(c.Suppression.Table, c.Ajout.Table, StringComparison.Ordinal)
            ? ResultatPredicat.Trouve("même table")
            : ResultatPredicat.Absent($"{c.Suppression.Table} / {c.Ajout.Table}");
    }
}

public sealed class MemeMigration : IPredicat
{
    public string Nom => "meme_migration";
    public string Version => "1.0";
    public string Libelle => "même migration";
    public string Cible => Cibles.Paire;

    public ResultatPredicat Evaluer(object cible, Faits faits)
    {
        var c = (CiblePaire)cible;
        return c.Suppression.Migration == c.Ajout.Migration
            ? ResultatPredicat.Trouve("même migration")
            : ResultatPredicat.Absent($"{c.Suppression.Migration} / {c.Ajout.Migration}");
    }
}

/// <summary>Types SQL Server compatibles : même famille, longueur et précision non réduites,
/// pas de passage d'une colonne nullable à une colonne NOT NULL.</summary>
public sealed class TypesCompatibles : IPredicat
{
    public string Nom => "types_compatibles";
    public string Version => "1.0";
    public string Libelle => "types compatibles";
    public string Cible => Cibles.Paire;

    private static readonly Regex Forme = new(@"^\s*(\w+)\s*(?:\(\s*(max|\d+)\s*(?:,\s*(\d+)\s*)?\))?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly string[][] Familles =
    {
        new[] { "char", "varchar", "nchar", "nvarchar" },
        new[] { "tinyint", "smallint", "int", "bigint" },
        new[] { "date", "smalldatetime", "datetime", "datetime2", "datetimeoffset" },
        new[] { "decimal", "numeric" },
        new[] { "real", "float" },
    };

    public ResultatPredicat Evaluer(object cible, Faits faits)
    {
        var c = (CiblePaire)cible;
        var (typeAvant, nullAvant) = TypeSupprime(c.Suppression, faits);
        if (typeAvant is null) return ResultatPredicat.NonVerifiable("type de la colonne supprimée inconnu");
        if (c.Ajout.TypeColonne is null) return ResultatPredicat.NonVerifiable("type de la colonne ajoutée inconnu");
        var paire = $"{typeAvant} → {c.Ajout.TypeColonne}";
        if (!Compatibles(typeAvant, c.Ajout.TypeColonne))
            return ResultatPredicat.Absent($"{typeAvant} / {c.Ajout.TypeColonne}");
        if (nullAvant == true && c.Ajout.Nullable == false)
            return ResultatPredicat.Absent($"{typeAvant} NULL / {c.Ajout.TypeColonne} NOT NULL");
        return ResultatPredicat.Trouve($"types compatibles ({paire})", $"types compatibles ({paire})");
    }

    /// <summary>Le type supprimé est lu dans Down (recréation par EF Core), sinon dans le modèle de N-1.</summary>
    private static (string? Type, bool? Nullable) TypeSupprime(OperationMigration s, Faits faits)
    {
        var down = faits.ToutesOperationsN.FirstOrDefault(o => o.Migration == s.Migration && o.Methode == "Down"
            && o.Type == "AddColumn" && o.Table == s.Table && o.Colonne == s.Colonne);
        if (down?.TypeColonne is not null) return (down.TypeColonne, down.Nullable);
        var p = faits.ModelePrecedent?.FirstOrDefault(m => m.Table == s.Table && m.Colonne == s.Colonne);
        return (p?.TypeColonne, p?.Nullable);
    }

    private static bool Compatibles(string avant, string apres)
    {
        var a = Forme.Match(avant);
        var b = Forme.Match(apres);
        if (!a.Success || !b.Success) return false;
        var ta = a.Groups[1].Value.ToLowerInvariant();
        var tb = b.Groups[1].Value.ToLowerInvariant();
        if (ta == tb && !a.Groups[2].Success && !b.Groups[2].Success) return true;
        var famille = Familles.FirstOrDefault(f => f.Contains(ta));
        if (famille is null || !famille.Contains(tb)) return ta == tb && a.Groups[2].Value == b.Groups[2].Value;
        if (famille[0] == "char")
        {
            if (ta.StartsWith('n') && !tb.StartsWith('n')) return false; // perte de l'Unicode
            return Longueur(b.Groups[2].Value) >= Longueur(a.Groups[2].Value);
        }
        if (famille[0] == "decimal")
            return int.Parse(b.Groups[2].Value) >= int.Parse(a.Groups[2].Value) && int.Parse(b.Groups[3].Value) >= int.Parse(a.Groups[3].Value);
        return Array.IndexOf(famille, tb) >= Array.IndexOf(famille, ta);
    }

    private static int Longueur(string v) => v.Equals("max", StringComparison.OrdinalIgnoreCase) || v.Length == 0 ? int.MaxValue : int.Parse(v);
}

/// <summary>Remplacement apparent dans le modèle : entre les instantanés de N-1 et de N, la même
/// entité perd l'ancienne propriété et gagne la nouvelle, avec un type de colonne et une
/// nullabilité identiques. Ne prouve ni l'identité métier ni le transfert des usages.</summary>
public sealed class RemplacementApparentDansModele : IPredicat
{
    public string Nom => "remplacement_apparent_dans_modele";
    public string Version => "1.0";
    public string Libelle => "remplacement apparent dans le modèle";
    public string Cible => Cibles.Paire;

    public ResultatPredicat Evaluer(object cible, Faits faits)
    {
        var c = (CiblePaire)cible;
        if (!faits.Precedente.FichiersDisponibles) return ResultatPredicat.NonVerifiable(faits.Precedente.RaisonFichiers!);
        if (faits.ModelePrecedent is null) return ResultatPredicat.NonVerifiable($"instantané du modèle absent de {faits.Precedente.Sha}");
        if (faits.ModeleN is null) return ResultatPredicat.NonVerifiable($"instantané du modèle absent de {faits.ShaN}");
        var avant = faits.ModelePrecedent.FirstOrDefault(p => p.Table == c.Suppression.Table && p.Colonne == c.Suppression.Colonne);
        var apres = faits.ModeleN.FirstOrDefault(p => p.Table == c.Ajout.Table && p.Colonne == c.Ajout.Colonne);
        if (avant is null || apres is null || avant.Entite != apres.Entite) return ResultatPredicat.Absent("propriétés non retrouvées sur la même entité");
        if (faits.ModeleN.Any(p => p.Entite == avant.Entite && p.Propriete == avant.Propriete)) return ResultatPredicat.Absent($"{avant.EntiteCourte}.{avant.Propriete} toujours présente dans {faits.ShaN}");
        if (faits.ModelePrecedent.Any(p => p.Entite == apres.Entite && p.Propriete == apres.Propriete)) return ResultatPredicat.Absent($"{apres.EntiteCourte}.{apres.Propriete} déjà présente dans {faits.Precedente.Sha}");
        if (avant.TypeColonne != apres.TypeColonne || avant.Nullable != apres.Nullable)
            return ResultatPredicat.Absent($"colonnes différentes ({avant.TypeColonne} / {apres.TypeColonne})");
        var t = $"{avant.EntiteCourte}.{avant.Propriete} → {apres.EntiteCourte}.{apres.Propriete}";
        return ResultatPredicat.Trouve($"remplacement apparent dans le modèle ({t})", $"remplacement apparent dans le modèle : {t}",
            new Dictionary<string, string> { ["entite"] = avant.Entite, ["avant"] = avant.Propriete, ["apres"] = apres.Propriete });
    }
}

/// <summary>Usages transférés entre N-1 et N : même membre (symbole Roslyn), propriétés résolues
/// par le modèle sémantique, instruction de N équivalente à celle de N-1 après substitution,
/// plus aucune référence à l'ancienne propriété dans N. Toute ambiguïté donne NON_VERIFIABLE.</summary>
public sealed class UsagesTransferes : IPredicat
{
    public string Nom => "usages_transferes";
    public string Version => "1.0";
    public string Libelle => "usages transférés";
    public string Cible => Cibles.Paire;

    public ResultatPredicat Evaluer(object cible, Faits faits)
    {
        var c = (CiblePaire)cible;
        if (!faits.Precedente.FichiersDisponibles) return ResultatPredicat.NonVerifiable(faits.Precedente.RaisonFichiers!);
        var entite = faits.ModeleN?.FirstOrDefault(p => p.Table == c.Ajout.Table && p.Colonne == c.Ajout.Colonne);
        var ancienne = faits.ModelePrecedent?.FirstOrDefault(p => p.Table == c.Suppression.Table && p.Colonne == c.Suppression.Colonne);
        if (entite is null || ancienne is null) return ResultatPredicat.NonVerifiable("propriétés introuvables dans les instantanés du modèle");
        var r = faits.Usages.Comparer(entite.Entite, ancienne.Propriete, entite.Propriete);
        if (r.Etat == Etat.NON_VERIFIABLE) return ResultatPredicat.NonVerifiable(r.Raison);
        if (r.Etat == Etat.ABSENT) return ResultatPredicat.Absent(r.Raison);
        var liste = string.Join(", ", r.Paires.Select(p => $"{p.AvantFichier}:{p.AvantLigne} → {p.ApresFichier}:{p.ApresLigne}"));
        return ResultatPredicat.Trouve(
            $"usages transférés ({r.Paires.Count} paires)",
            $"usages transférés de {ancienne.Propriete} vers {entite.Propriete} entre {faits.Precedente.Sha} et {faits.ShaN} : {liste}",
            new Dictionary<string, string> { ["paires"] = r.Paires.Count.ToString() });
    }
}
