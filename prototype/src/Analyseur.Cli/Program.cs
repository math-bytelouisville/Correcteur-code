using System.Diagnostics;
using Analyseur.Moteur;

// analyseur preparer <solution.sln> [--revision <sha>] [--cache <dossier>] [--hors-ligne]
// analyseur analyser <solution.sln> [--revision-precedente <sha>] [--regles <dossier>] [--sortie <rapport.json>]
// analyseur regles ajouter <regle.json> [--regles <dossier>]

var outil = AppContext.BaseDirectory;
var reglesParDefaut = Path.GetFullPath(Path.Combine(outil, "regles"));

try
{
    return args switch
    {
        ["preparer", var sln, .. var reste] => Preparer(sln, reste),
        ["analyser", var sln, .. var reste] => Analyser(sln, reste),
        ["regles", "ajouter", var fichier, .. var reste] => AjouterRegle(fichier, reste),
        _ => Aide(),
    };
}
catch (ReglesRefusees e)
{
    Console.WriteLine(e.Message);
    return 2;
}

static string? Option(string[] a, string nom) { var i = Array.IndexOf(a, nom); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; }

int Aide()
{
    Console.WriteLine("Usage : analyseur preparer|analyser <solution.sln> … | analyseur regles ajouter <regle.json>");
    return 1;
}

int Preparer(string sln, string[] a)
{
    var racine = Path.GetDirectoryName(Path.GetFullPath(sln))!;
    var revision = Option(a, "--revision");
    var horsLigne = a.Contains("--hors-ligne");
    var cache = Path.GetFullPath(Option(a, "--cache") ?? Path.Combine(EtatsPreparation.Dossier(racine), "cache"));
    string cible, cle;
    if (revision is null)
    {
        cible = racine;
        cle = "courante";
    }
    else
    {
        var sha = Git.ShaCourt(racine, revision) ?? throw new ReglesRefusees($"Révision {revision} introuvable.");
        cible = EtatsPreparation.DossierRevision(racine, sha);
        cle = sha;
        if (!Directory.Exists(cible))
        {
            var (code, sortie) = Git.Executer(racine, "worktree", "add", "--detach", cible, sha);
            if (code != 0) { Console.WriteLine($"Préparation impossible : {sortie}"); return 1; }
        }
    }
    var solution = Path.Combine(cible, Path.GetFileName(sln));
    var psi = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var x in new[] { "restore", solution, "--packages", cache }) psi.ArgumentList.Add(x);
    if (horsLigne)
    {
        // Aucune source distante : seuls les paquets déjà présents dans le cache donné sont utilisables.
        var vide = Path.Combine(EtatsPreparation.Dossier(racine), "source-vide");
        Directory.CreateDirectory(vide);
        foreach (var x in new[] { "--source", vide, "-p:RestoreFallbackFolders=clear", "-p:RestoreAdditionalProjectFallbackFolders=" }) psi.ArgumentList.Add(x);
    }
    using var p = Process.Start(psi)!;
    var texte = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
    p.WaitForExit();
    var etat = new EtatPreparation
    {
        Sha = cle, HorsLigne = horsLigne,
        Restauration = p.ExitCode == 0 ? "ok" : horsLigne ? "echec_hors_ligne" : "echec",
        Erreur = p.ExitCode == 0 ? null : texte.Split('\n').FirstOrDefault(l => l.Contains("error", StringComparison.OrdinalIgnoreCase))?.Trim(),
    };
    EtatsPreparation.Ecrire(racine, cle, etat);
    Console.WriteLine(p.ExitCode == 0
        ? $"Révision {cle} préparée (paquets restaurés{(horsLigne ? " hors ligne" : "")})."
        : $"Révision {cle} : restauration des paquets impossible{(horsLigne ? " hors ligne" : "")} ({etat.Erreur}).");
    return p.ExitCode == 0 ? 0 : 3;
}

int Analyser(string sln, string[] a)
{
    var racine = Path.GetDirectoryName(Path.GetFullPath(sln))!;
    var dossierRegles = Path.GetFullPath(Option(a, "--regles") ?? reglesParDefaut);
    var registre = RegistrePredicats.Charger(outil);
    var avertissements = new List<string>();
    var regles = ChargeurRegles.ChargerDepuisVerrou(dossierRegles, registre, avertissements);

    var shaN = Git.ShaCourt(racine) ?? "copie de travail";
    var prec = Revisions.Resoudre(racine, Option(a, "--revision-precedente"));
    var opsN = ExtracteurMigrations.Extraire(racine);
    var opsP = prec.FichiersDisponibles ? ExtracteurMigrations.Extraire(prec.Racine!) : null;
    var faits = new Faits
    {
        RacineN = racine,
        ShaN = shaN,
        Precedente = prec,
        Operations = ExtracteurMigrations.Nouvelles(opsN, opsP),
        ToutesOperationsN = opsN,
        ModeleN = ExtracteurModele.Extraire(racine),
        ModelePrecedent = prec.FichiersDisponibles ? ExtracteurModele.Extraire(prec.Racine!) : null,
        Usages = new AnalyseurUsages(racine, shaN, prec),
    };
    var resultat = MoteurRegles.Evaluer(regles, registre, faits);

    // Le commit n'est cité que si le verrou est suivi par Git et identique à ce commit.
    var suivi = Git.Executer(dossierRegles, "ls-files", "--error-unmatch", ChargeurRegles.NomVerrou).Code == 0
        && Git.Executer(dossierRegles, "diff", "--quiet", "HEAD", "--", ".").Code == 0;
    var commit = suivi && Git.ShaCourt(dossierRegles) is { } c ? $"au commit {c}" : "(hors dépôt Git ou modifié depuis le dernier commit)";
    var ctx = new Rapport.Contexte(shaN, prec.Sha, prec.RaisonFichiers ?? "", commit, regles, avertissements);
    Console.Write(Rapport.Texte(resultat, ctx));
    if (Option(a, "--sortie") is { } sortie) File.WriteAllText(sortie, Rapport.Json(resultat, ctx, DateTimeOffset.Now));
    return 0;
}

int AjouterRegle(string fichier, string[] a)
{
    var dossierRegles = Path.GetFullPath(Option(a, "--regles") ?? reglesParDefaut);
    var registre = RegistrePredicats.Charger(outil);
    var regle = ChargeurRegles.LireEtValider(fichier, registre);
    var destination = Path.Combine(dossierRegles, Path.GetFileName(fichier));
    if (Path.GetFullPath(fichier) != destination) File.Copy(fichier, destination, overwrite: true);
    var verrou = ChargeurRegles.LireVerrou(dossierRegles);
    verrou.Regles.RemoveAll(e => e.Id == regle.Id);
    verrou.Regles.Add(new EntreeVerrou { Id = regle.Id, Version = regle.Version, Fichier = Path.GetFileName(fichier), Etat = "CANDIDATE", Sha256 = ChargeurRegles.Empreinte(destination) });
    ChargeurRegles.EcrireVerrou(dossierRegles, verrou);
    var preds = string.Join(", ", regle.PredicatsRequis.Select(p => $"{p.Key} {p.Value}"));
    Console.WriteLine($"Règle {regle.Id} v{regle.Version} ({regle.Forme}) ajoutée en CANDIDATE — prédicats : {preds}.");
    return 0;
}
