using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Analyseur.Moteur;

/// <summary>Rendu à l'écran et rapport JSON. Aucun chemin absolu, aucune donnée volatile hors
/// du champ « horodatage ».</summary>
public static class Rapport
{
    public sealed record Contexte(string ShaN, string? ShaPrecedente, string RaisonPrecedente, string CommitRegles,
        IReadOnlyList<Regle> Regles, IReadOnlyList<string> Avertissements);

    public static string Texte(Resultat r, Contexte ctx)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Assistant indisponible : explications désactivées.");
        sb.AppendLine($"Règles actives exécutées depuis {ChargeurRegles.NomVerrou} {ctx.CommitRegles}.");
        sb.AppendLine($"Révision analysée : {ctx.ShaN} ; révision précédente : {ctx.ShaPrecedente ?? ctx.RaisonPrecedente}.");
        foreach (var a in ctx.Avertissements) sb.AppendLine(a);
        sb.AppendLine();
        sb.AppendLine("== Vue principale ==");
        if (r.Alertes.Count == 0 && r.HypothesesSeules.All(h => h.Placement != "principal")) sb.AppendLine("Aucune alerte.");
        foreach (var a in r.Alertes)
        {
            sb.AppendLine($"{a.Entete} : {a.Message}");
            foreach (var q in a.Questions) sb.AppendLine("    " + q.Texte);
        }
        foreach (var h in r.HypothesesSeules.Where(h => h.Placement == "principal")) sb.AppendLine(h.Texte);
        sb.AppendLine();
        sb.AppendLine("== Détail ==");
        var vide = true;
        foreach (var a in r.Alertes.Where(a => a.HypothesesDetail.Count > 0 || a.Detail.Count > 0))
        {
            vide = false;
            sb.AppendLine($"{a.Regle.Id} — {a.Cible.Fichier} ligne {a.Cible.Ligne} :");
            foreach (var h in a.HypothesesDetail) sb.AppendLine("    " + h.Texte);
            foreach (var d in a.Detail) sb.AppendLine("    " + d);
        }
        foreach (var h in r.HypothesesSeules.Where(h => h.Placement == "detail")) { vide = false; sb.AppendLine(h.Texte); }
        if (vide) sb.AppendLine("Aucun détail.");
        sb.AppendLine();
        sb.AppendLine("== Règles évaluées sans alerte ==");
        if (r.Silencieuses.Count == 0) sb.AppendLine("Aucune.");
        foreach (var s in r.Silencieuses) sb.AppendLine(s);
        sb.AppendLine();
        sb.AppendLine("== Candidates ==");
        if (r.Candidates.Count == 0) sb.AppendLine("Aucune.");
        foreach (var h in r.Candidates) sb.AppendLine($"[CANDIDATE {h.Regle.Id} v{h.Regle.Version}] {h.Texte}");
        return sb.ToString();
    }

    public static string Json(Resultat r, Contexte ctx, DateTimeOffset horodatage)
    {
        JsonObject Preds(Dictionary<string, ResultatPredicat> p)
        {
            var o = new JsonObject();
            foreach (var (k, v) in p)
                o[k] = new JsonObject { ["etat"] = v.Etat.ToString(), ["resume"] = v.Resume, ["raison"] = v.Raison };
            return o;
        }
        JsonObject Hyp(Hypothese h) => new()
        {
            ["regle"] = h.Regle.Id, ["version"] = h.Regle.Version, ["niveau"] = "HYPOTHESE", ["placement"] = h.Placement,
            ["suppression"] = $"{h.Cible.Suppression.Table}.{h.Cible.Suppression.Colonne}",
            ["ajout"] = $"{h.Cible.Ajout.Table}.{h.Cible.Ajout.Colonne}",
            ["texte"] = h.Texte, ["predicats"] = Preds(h.Predicats),
        };
        var alertes = new JsonArray();
        foreach (var a in r.Alertes)
            alertes.Add(new JsonObject
            {
                ["regle"] = a.Regle.Id, ["version"] = a.Regle.Version, ["gravite"] = a.Regle.Gravite, ["niveau"] = a.Regle.Niveau,
                ["fichier"] = a.Cible.Fichier, ["ligne"] = a.Cible.Ligne, ["message"] = a.Message,
                ["predicats"] = Preds(a.Predicats),
                ["questions"] = new JsonArray(a.Questions.Select(q => (JsonNode)Hyp(q)).ToArray()),
                ["hypotheses_detail"] = new JsonArray(a.HypothesesDetail.Select(q => (JsonNode)Hyp(q)).ToArray()),
                ["detail"] = new JsonArray(a.Detail.Select(d => (JsonNode)JsonValue.Create(d)!).ToArray()),
            });
        var racine = new JsonObject
        {
            ["format"] = "1.0",
            ["horodatage"] = horodatage.ToString("O"),
            ["revision"] = ctx.ShaN,
            ["revision_precedente"] = ctx.ShaPrecedente,
            ["revision_precedente_raison"] = ctx.ShaPrecedente is null ? ctx.RaisonPrecedente : null,
            ["regles"] = new JsonArray(ctx.Regles.Select(x => (JsonNode)new JsonObject { ["id"] = x.Id, ["version"] = x.Version, ["etat"] = x.EtatCycle }).ToArray()),
            ["avertissements"] = new JsonArray(ctx.Avertissements.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray()),
            ["alertes"] = alertes,
            ["hypotheses_sans_alerte"] = new JsonArray(r.HypothesesSeules.Select(h => (JsonNode)Hyp(h)).ToArray()),
            ["evaluations_silencieuses"] = new JsonArray(r.Silencieuses.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray()),
            ["candidates"] = new JsonArray(r.Candidates.Select(h => (JsonNode)Hyp(h)).ToArray()),
        };
        return racine.ToJsonString(ChargeurRegles.OptionsEcriture) + "\n";
    }
}
