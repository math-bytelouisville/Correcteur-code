using System.Text.RegularExpressions;

namespace Analyseur.Moteur;

public sealed class Hypothese
{
    public required Regle Regle { get; init; }
    public required string Placement { get; init; }  // principal, detail, candidate
    public required string Texte { get; init; }
    public required Dictionary<string, ResultatPredicat> Predicats { get; init; }
    public required CiblePaire Cible { get; init; }
}

public sealed class Alerte
{
    public required Regle Regle { get; init; }
    public required OperationMigration Cible { get; init; }
    public required string Message { get; init; }
    public required Dictionary<string, ResultatPredicat> Predicats { get; init; }
    public List<string> Detail { get; } = new();
    public List<Hypothese> Questions { get; } = new();
    public List<Hypothese> HypothesesDetail { get; } = new();

    public string Entete => $"[{Regle.Gravite}][{Regle.Niveau}] {Regle.Id} v{Regle.Version} — {Cible.Fichier} ligne {Cible.Ligne}";
}

public sealed class Resultat
{
    public List<Alerte> Alertes { get; } = new();
    public List<string> Silencieuses { get; } = new();
    public List<Hypothese> Candidates { get; } = new();
    public List<Hypothese> HypothesesSeules { get; } = new();
}

/// <summary>Étage 2 : applique les règles aux faits. La politique d'affichage est la même pour
/// toutes les règles ; les indices requis viennent de chaque règle versionnée.</summary>
public static class MoteurRegles
{
    public static Resultat Evaluer(IReadOnlyList<Regle> regles, RegistrePredicats registre, Faits faits)
    {
        var res = new Resultat();
        var suppressions = faits.Operations.Where(o => o.Methode == "Up" && o.Type == "DropColumn").ToList();
        var ajouts = faits.Operations.Where(o => o.Methode == "Up" && o.Type == "AddColumn").ToList();

        foreach (var regle in regles.Where(r => r.Niveau == "CONSTAT" && r.Cible == Cibles.Suppression))
            foreach (var s in suppressions)
            {
                var cible = new CibleSuppression(s);
                var preds = regle.PredicatsRequis.Keys.ToDictionary(k => k, k => registre.Trouver(k)!.Evaluer(cible, faits));
                var message = Remplir(regle.Message!.Base, s.Table, s.Colonne, null, null, preds);
                foreach (var (nom, parEtat) in regle.Message.Selon)
                    if (parEtat.TryGetValue(preds[nom].Etat.ToString(), out var t))
                        message += " " + Remplir(t, s.Table, s.Colonne, null, null, preds);
                var alerte = new Alerte { Regle = regle, Cible = s, Message = message, Predicats = preds };
                foreach (var p in preds.Values) if (p.Detail is not null) alerte.Detail.AddRange(p.Detail);
                if (regle.Active) res.Alertes.Add(alerte);
            }

        foreach (var regle in regles.Where(r => r.Niveau == "HYPOTHESE" && r.Cible == Cibles.Paire))
        {
            var paires = suppressions.SelectMany(s => ajouts.Select(a => new CiblePaire(s, a))).ToList();
            if (paires.Count == 0)
            {
                res.Silencieuses.Add($"{regle.Id} v{regle.Version} non déclenchée — aucune paire suppression/ajout dans les migrations analysées.");
                continue;
            }
            foreach (var paire in paires)
            {
                var preds = new Dictionary<string, ResultatPredicat>(StringComparer.Ordinal);
                string? echec = null;
                foreach (var c in regle.ConditionsObligatoires!)
                {
                    var r = registre.Trouver(c)!.Evaluer(paire, faits);
                    preds[c] = r;
                    if (r.Etat != Etat.TROUVE) { echec = $"{c} : {r.Etat} ({r.Raison})"; break; }
                }
                if (echec is not null)
                {
                    res.Silencieuses.Add($"{regle.Id} v{regle.Version} non déclenchée — {echec}.");
                    continue;
                }
                foreach (var c in (regle.AffichagePrincipalSi ?? new()).Concat(regle.IndicesComplementaires ?? new()))
                    preds[c] = registre.Trouver(c)!.Evaluer(paire, faits);

                var principal = (regle.AffichagePrincipalSi ?? new()).All(c => preds[c].Etat == Etat.TROUVE);
                var placement = !regle.Active ? "candidate" : principal ? "principal" : "detail";
                var texte = placement == "principal" ? TextePrincipal(regle, paire, preds, registre) : TexteDetail(regle, paire, preds, registre);
                var h = new Hypothese { Regle = regle, Placement = placement, Texte = texte, Predicats = preds, Cible = paire };
                if (placement == "candidate") { res.Candidates.Add(h); continue; }
                var hote = res.Alertes.FirstOrDefault(a => a.Regle.Id == regle.Rattachement && a.Cible == paire.Suppression);
                if (hote is null) res.HypothesesSeules.Add(h);
                else if (placement == "principal") hote.Questions.Add(h);
                else hote.HypothesesDetail.Add(h);
            }
        }
        return res;
    }

    private static string TextePrincipal(Regle r, CiblePaire c, Dictionary<string, ResultatPredicat> preds, RegistrePredicats reg)
    {
        var parties = (r.AffichagePrincipalSi ?? new()).Concat(r.IndicesComplementaires ?? new()).Select(nom =>
        {
            var p = preds[nom];
            var libelle = reg.Trouver(nom)!.Libelle;
            return p.Etat switch
            {
                Etat.TROUVE => p.Developpe!,
                Etat.ABSENT => $"{libelle} : absent",
                _ => $"{libelle} : non vérifiable ({p.Raison})",
            };
        });
        var t = $"{Remplir(r.QuestionPrincipale!, c, preds)} ({r.Id} v{r.Version}, HYPOTHÈSE) — {string.Join(" ; ", parties)}.";
        if (r.Inconnu is not null) t += $" Non vérifiable par l'outil : {Remplir(r.Inconnu, c, preds)}.";
        if (r.Correction is not null) t += $" Correction proposée : {Remplir(r.Correction, c, preds)}.";
        return t;
    }

    private static string TexteDetail(Regle r, CiblePaire c, Dictionary<string, ResultatPredicat> preds, RegistrePredicats reg)
    {
        var t = $"{Remplir(r.IntituleDetail!, c, preds)} ({r.Id} v{r.Version}, HYPOTHÈSE faible).";
        var trouves = preds.Where(p => p.Value.Etat == Etat.TROUVE).Select(p => p.Value.Resume).ToList();
        var absents = preds.Where(p => p.Value.Etat == Etat.ABSENT).Select(p => reg.Trouver(p.Key)!.Libelle).ToList();
        if (trouves.Count > 0) t += $" Indices trouvés : {string.Join(" ; ", trouves)}.";
        if (absents.Count > 0) t += $" Indices absents : {string.Join(" ; ", absents)}.";
        foreach (var (nom, p) in preds.Where(p => p.Value.Etat == Etat.NON_VERIFIABLE))
            t += $" {Majuscule(reg.Trouver(nom)!.Libelle)} : non vérifiable ({p.Raison}).";
        return t;
    }

    private static string Majuscule(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static string Remplir(string modele, CiblePaire c, Dictionary<string, ResultatPredicat> preds)
        => Remplir(modele, c.Suppression.Table, c.Suppression.Colonne, c.Suppression.Colonne, c.Ajout.Colonne, preds);

    private static string Remplir(string modele, string? table, string? colonne, string? ancienne, string? nouvelle, Dictionary<string, ResultatPredicat> preds)
        => Regex.Replace(modele, @"\{([^{}]*)\}", m =>
        {
            var nom = m.Groups[1].Value;
            switch (nom)
            {
                case "table": return table ?? "?";
                case "colonne": return colonne ?? "?";
                case "ancienne": return ancienne ?? "?";
                case "nouvelle": return nouvelle ?? "?";
            }
            var point = nom.IndexOf('.');
            if (point > 0 && preds.TryGetValue(nom[..point], out var p))
            {
                var cle = nom[(point + 1)..];
                if (cle == "raison") return p.Raison ?? "?";
                if (p.Valeurs is not null && p.Valeurs.TryGetValue(cle, out var v)) return v;
            }
            return "?";
        });
}
