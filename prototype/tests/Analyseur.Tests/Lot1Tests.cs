using System.Text.Json.Nodes;
using static Analyseur.Tests.Outils;

namespace Analyseur.Tests;

/// <summary>Premier lot convenu dans revues/ : T-001, T-002, T-009, T-006. Projets entièrement
/// fictifs, sans modèle de langage. Les textes attendus sont comparés mot pour mot.</summary>
public class Lot1Tests
{
    private const string Base = "Après son application, ses valeurs ne seront plus accessibles dans cette colonne.";
    private const string SansCopie = "Aucune copie de ces valeurs n'a été trouvée dans cette migration. Vérifiez où elles doivent être conservées.";

    private static string Mig001(string fichier, string colonne, string suite)
        => $"[ERREUR][CONSTAT] MIG-001 v1.0.0 — Migrations/{fichier} ligne 14 : Cette migration supprime la colonne Clients.{colonne}. {Base} {suite}";

    [Fact]
    public void T001_renommage_etaye_question_sous_le_constat()
    {
        var d = Prepare("T-001-renommage");
        var rapport = Path.GetTempFileName();
        var s = Analyser(d, rapport: rapport);

        Assert.Equal(new[]
        {
            Mig001("20261125093000_ClientNomComplet.cs", "Nom", SansCopie),
            $"    Vouliez-vous renommer cette colonne ? (REN-001 v1.0.0, HYPOTHÈSE) — usages transférés de Nom vers NomComplet entre {d.ShaPrecedent} et {d.ShaCourant} : "
            + "ClientService.cs:30 → ClientService.cs:30, ClientsController.cs:42 → ClientsController.cs:42, Edit.cshtml:12 → Edit.cshtml:12, Index.cshtml:17 → Index.cshtml:17 ; "
            + "remplacement apparent dans le modèle : Client.Nom → Client.NomComplet. Non vérifiable par l'outil : votre intention de conserver les valeurs. "
            + "Correction proposée : RenameColumn(name: 'Nom', table: 'Clients', newName: 'NomComplet').",
        }, Lignes(Section(s.Texte, "Vue principale")));
        Assert.Equal("Aucun détail.", Section(s.Texte, "Détail"));

        var json = JsonNode.Parse(File.ReadAllText(rapport))!;
        var alerte = json["alertes"]![0]!;
        Assert.Equal("CONSTAT", (string)alerte["niveau"]!);
        Assert.Equal("ERREUR", (string)alerte["gravite"]!);
        Assert.Equal("ABSENT", (string)alerte["predicats"]!["copie_avant_suppression"]!["etat"]!);
        var question = alerte["questions"]![0]!;
        Assert.Equal("HYPOTHESE", (string)question["niveau"]!);
        Assert.Equal("TROUVE", (string)question["predicats"]!["usages_transferes"]!["etat"]!);
        Assert.Equal("usages transférés (4 paires)", (string)question["predicats"]!["usages_transferes"]!["resume"]!);
        // Mode analyse : aucun fichier suivi n'est modifié.
        Assert.Equal("", Git(d.Racine, "status", "--porcelain"));
    }

    [Fact]
    public void T002_A_suppression_et_ajout_independants()
    {
        var s = Analyser(Prepare("T-002-A-fax"));
        Assert.Equal(new[] { Mig001("20261125100000_FaxVersDateInscription.cs", "Fax", SansCopie) }, Lignes(Section(s.Texte, "Vue principale")));
        Assert.Equal("Aucun détail.", Section(s.Texte, "Détail"));
        Assert.Equal("REN-001 v1.0.0 non déclenchée — types_compatibles : ABSENT (nvarchar(20) / datetime2).", Section(s.Texte, "Règles évaluées sans alerte"));
        Assert.DoesNotContain("renommer", s.Texte);
        Assert.DoesNotContain("Renommage", s.Texte);
    }

    private const string CopieTrouvee = "Ligne 12, une instruction copie Clients.Nom vers dbo.ClientsArchive.Nom avant la suppression. La conservation de chaque valeur pour le même identifiant reste à vérifier.";

    [Fact]
    public void T002_B_copie_reconnue_reste_en_erreur()
    {
        var s = Analyser(Prepare("T-002-B-archive"));
        Assert.Equal(new[] { Mig001("20261125110000_ArchiverNom.cs", "Nom", CopieTrouvee) }, Lignes(Section(s.Texte, "Vue principale")));
        Assert.Equal("Aucun détail.", Section(s.Texte, "Détail"));
        Assert.Equal("REN-001 v1.0.0 non déclenchée — aucune paire suppression/ajout dans les migrations analysées.", Section(s.Texte, "Règles évaluées sans alerte"));
    }

    [Fact]
    public void T002_B_filtre_copie_partielle_signalee_dans_le_detail()
    {
        var s = Analyser(Prepare("T-002-B-filtre"));
        Assert.Equal(new[] { Mig001("20261125110000_ArchiverNom.cs", "Nom", CopieTrouvee) }, Lignes(Section(s.Texte, "Vue principale")));
        Assert.Equal(new[]
        {
            "MIG-001 — Migrations/20261125110000_ArchiverNom.cs ligne 14 :",
            "    Filtre appliqué à la copie : WHERE Actif = 1. La couverture de la copie n'est pas vérifiée.",
        }, Lignes(Section(s.Texte, "Détail")));
    }

    [Fact]
    public void T002_temoin_lecture_dans_une_condition_n_est_pas_une_copie()
    {
        var s = Analyser(Prepare("T-002-temoin"));
        Assert.Equal(new[] { Mig001("20261125110000_ArchiverNom.cs", "Nom", SansCopie) }, Lignes(Section(s.Texte, "Vue principale")));
        Assert.Equal(new[]
        {
            "MIG-001 — Migrations/20261125110000_ArchiverNom.cs ligne 14 :",
            "    Clients.Nom est lu ligne 12 dans une condition, sans être copié.",
        }, Lignes(Section(s.Texte, "Détail")));
    }

    [Fact]
    public void T002_C_sql_dynamique_non_verifiable()
    {
        var s = Analyser(Prepare("T-002-C-dynamique"));
        Assert.Equal(new[]
        {
            Mig001("20261125110000_ArchiverNom.cs", "Nom", "Copie avant la suppression : non vérifiable (texte SQL construit dynamiquement, ligne 12). Vérifiez où elles doivent être conservées."),
        }, Lignes(Section(s.Texte, "Vue principale")));
    }

    private const string FauxIntitule = "    Renommage possible Clients.Ville → Clients.Commentaire (REN-001 v1.0.0, HYPOTHÈSE faible). Indices trouvés : même table ; même migration ; types compatibles (nvarchar(50) → nvarchar(100)).";
    private const string VilleSansCopie = "[ERREUR][CONSTAT] MIG-001 v1.0.0 — Migrations/20261125120000_VilleVersCommentaire.cs ligne 14 : Cette migration supprime la colonne Clients.Ville. " + Base + " " + SansCopie;

    [Fact]
    public void T009_predicat_inconnu_refuse()
    {
        var regle = Path.Combine(Path.GetTempPath(), "REN-003.json");
        File.WriteAllText(regle, """
            {
              "id": "REN-003", "version": "0.1.0", "forme": "json", "categorie": "MIGRATION_DESTRUCTIVE",
              "niveau_epistemique": "HYPOTHESE", "cible": "paire_suppression_ajout",
              "predicats_requis": { "meme_table": "1.0", "executer_sql": "1.0" },
              "conditions_obligatoires": ["meme_table", "executer_sql"],
              "intitule_detail": "Essai", "question_principale": "Essai ?"
            }
            """);
        var regles = ReglesAvecEtat("ACTIVE_DETECTION");
        var avant = File.ReadAllText(Path.Combine(regles, "regles.lock.json"));
        var s = Lancer(Prototype, false, "regles", "ajouter", regle, "--regles", regles);
        Assert.Equal(2, s.Code);
        Assert.Equal("Règle REN-003 refusée : prédicat inconnu executer_sql.", s.Texte.Trim());
        Assert.Equal(avant, File.ReadAllText(Path.Combine(regles, "regles.lock.json")));
    }

    [Fact]
    public void T009_candidate_reste_dans_la_vue_des_candidates()
    {
        var s = Analyser(Prepare("T-009-faux-renommage"), regles: ReglesAvecEtat("CANDIDATE"));
        Assert.Equal(new[] { VilleSansCopie }, Lignes(Section(s.Texte, "Vue principale")));
        Assert.Equal("Aucun détail.", Section(s.Texte, "Détail"));
        Assert.Equal(new[] { "[CANDIDATE REN-001 v1.0.0] " + FauxIntitule.Trim() + " Indices absents : usages transférés ; remplacement apparent dans le modèle." },
            Lignes(Section(s.Texte, "Candidates")));
    }

    [Fact]
    public void T009_faux_renommage_reste_dans_le_detail()
    {
        var s = Analyser(Prepare("T-009-faux-renommage"));
        Assert.Equal(new[] { VilleSansCopie }, Lignes(Section(s.Texte, "Vue principale")));
        Assert.Equal(new[]
        {
            "MIG-001 — Migrations/20261125120000_VilleVersCommentaire.cs ligne 14 :",
            FauxIntitule + " Indices absents : usages transférés ; remplacement apparent dans le modèle.",
        }, Lignes(Section(s.Texte, "Détail")));
    }

    [Fact]
    public void T009_sans_historique_non_verifiable()
    {
        var s = Analyser(Prepare("T-009-faux-renommage"), sansHistorique: true);
        Assert.Equal(new[] { VilleSansCopie }, Lignes(Section(s.Texte, "Vue principale")));
        Assert.Equal(new[]
        {
            "MIG-001 — Migrations/20261125120000_VilleVersCommentaire.cs ligne 14 :",
            FauxIntitule + " Usages transférés : non vérifiable (révision antérieure indisponible). Remplacement apparent dans le modèle : non vérifiable (révision antérieure indisponible).",
        }, Lignes(Section(s.Texte, "Détail")));
    }

    [Fact]
    public void T009_appariement_ambigu_non_verifiable()
    {
        var s = Analyser(Prepare("T-009-ambigu"));
        Assert.Equal(new[] { VilleSansCopie }, Lignes(Section(s.Texte, "Vue principale")));
        Assert.Equal(new[]
        {
            "MIG-001 — Migrations/20261125120000_VilleVersCommentaire.cs ligne 14 :",
            FauxIntitule + " Indices absents : remplacement apparent dans le modèle. Usages transférés : non vérifiable (appariement ambigu dans ClientService.Enregistrer : instructions non équivalentes après substitution).",
        }, Lignes(Section(s.Texte, "Détail")));
    }

    [Theory]
    [InlineData("T-001-renommage")]
    [InlineData("T-002-A-fax")]
    [InlineData("T-009-faux-renommage")]
    public void T006_rapport_identique_hors_ligne(string cas)
    {
        var d = Prepare(cas);
        var reference = Path.GetTempFileName();
        var horsLigne = Path.GetTempFileName();
        var enLigne = Analyser(d, rapport: reference);
        var coupe = Analyser(d, horsLigne: true, rapport: horsLigne);
        Assert.Equal(enLigne.Texte, coupe.Texte);
        Assert.Equal(SansHorodatage(reference), SansHorodatage(horsLigne));
        Assert.StartsWith("Assistant indisponible : explications désactivées.", coupe.Texte);
    }

    [Fact]
    public void T006_dependance_manquante_de_N_moins_1()
    {
        var reference = Prepare("T-001-renommage"); // garantit le cache commun rempli
        var d = Creer("T-001-renommage", "T-006-variante");
        Verifier(Lancer(d.Racine, false, "preparer", "ClientsDemo.sln", "--cache", CacheCommun));
        var cacheCommunAvant = Contenu(CacheCommun);
        var cacheVide = Path.Combine(d.Racine, ".analyseur", "cache-propre-N-1");
        var p = Lancer(d.Racine, true, "preparer", "ClientsDemo.sln", "--revision", d.ShaPrecedent, "--cache", cacheVide, "--hors-ligne");
        Assert.Equal(3, p.Code);
        Assert.StartsWith($"Révision {d.ShaPrecedent} : restauration des paquets impossible hors ligne", p.Texte);

        var s = Analyser(d, horsLigne: true);
        Assert.Equal(new[] { Mig001("20261125093000_ClientNomComplet.cs", "Nom", SansCopie) }, Lignes(Section(s.Texte, "Vue principale")));
        Assert.Equal(new[]
        {
            "MIG-001 — Migrations/20261125093000_ClientNomComplet.cs ligne 14 :",
            "    Renommage possible Clients.Nom → Clients.NomComplet (REN-001 v1.0.0, HYPOTHÈSE faible). Indices trouvés : même table ; même migration ; types compatibles (nvarchar(100) → nvarchar(100)) ; "
            + $"remplacement apparent dans le modèle (Client.Nom → Client.NomComplet). Usages transférés : non vérifiable (restauration des paquets impossible hors ligne pour {d.ShaPrecedent}).",
        }, Lignes(Section(s.Texte, "Détail")));
        Assert.Equal(cacheCommunAvant, Contenu(CacheCommun));
        Assert.NotNull(reference);
    }

    private static string Contenu(string dossier)
        => string.Join("\n", Directory.GetFileSystemEntries(dossier, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
}
