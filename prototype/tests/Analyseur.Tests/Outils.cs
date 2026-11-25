using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Analyseur.Tests;

/// <summary>Construit des dépôts Git fictifs à partir de exemples/ et lance l'outil comme le ferait
/// l'utilisateur. Tous les projets et toutes les valeurs sont fictifs.</summary>
public static class Outils
{
    public static readonly string Prototype = TrouverPrototype();
    public static readonly string Exemples = Path.Combine(Prototype, "exemples");
    public static readonly string Regles = Path.Combine(Prototype, "regles");
    /// <summary>Cache NuGet propre aux tests : le cache du poste n'est jamais utilisé ni modifié.</summary>
    public static readonly string CacheCommun = Path.Combine(Prototype, ".cache-tests", "nuget");
    private static readonly string Travail = Path.Combine(Path.GetTempPath(), "analyseur-tests-" + Environment.ProcessId);
    private static readonly ConcurrentDictionary<string, Lazy<Depot>> Depots = new();

    public sealed record Depot(string Racine, string ShaPrecedent, string ShaCourant);
    public sealed record Sortie(int Code, string Texte);

    private static string TrouverPrototype()
    {
        var d = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(d, "Analyseur.sln"))) d = Path.GetDirectoryName(d) ?? throw new InvalidOperationException("Analyseur.sln introuvable");
        return d;
    }

    private static string Cli()
    {
        var config = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}") ? "Release" : "Debug";
        return Path.Combine(Prototype, "src", "Analyseur.Cli", "bin", config, "net8.0", "analyseur.dll");
    }

    /// <summary>Dépôt préparé une fois par exécution des tests (révisions N et N-1 restaurées).</summary>
    public static Depot Prepare(string cas) => Depots.GetOrAdd(cas, c => new Lazy<Depot>(() =>
    {
        var d = Creer(c, c);
        Verifier(Lancer(d.Racine, false, "preparer", "ClientsDemo.sln", "--cache", CacheCommun));
        Verifier(Lancer(d.Racine, false, "preparer", "ClientsDemo.sln", "--revision", d.ShaPrecedent, "--cache", CacheCommun));
        return d;
    })).Value;

    /// <summary>Crée un dépôt : base + cas/avant (commit N-1), puis cas/apres (commit N).</summary>
    public static Depot Creer(string cas, string nomDossier)
    {
        var racine = Path.Combine(Travail, nomDossier);
        if (Directory.Exists(racine)) Directory.Delete(racine, true);
        Copier(Path.Combine(Exemples, "base"), racine);
        Copier(Path.Combine(Exemples, "cas", cas, "avant"), racine);
        Git(racine, "init", "-q");
        Git(racine, "add", "-A");
        Git(racine, "commit", "-q", "-m", "N-1");
        var precedent = Git(racine, "rev-parse", "--short=7", "HEAD");
        Copier(Path.Combine(Exemples, "cas", cas, "apres"), racine);
        Git(racine, "add", "-A");
        Git(racine, "commit", "-q", "-m", "N");
        return new(racine, precedent, Git(racine, "rev-parse", "--short=7", "HEAD"));
    }

    private static void Copier(string source, string destination)
    {
        if (!Directory.Exists(source)) return;
        foreach (var f in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var cible = Path.Combine(destination, Path.GetRelativePath(source, f));
            Directory.CreateDirectory(Path.GetDirectoryName(cible)!);
            File.Copy(f, cible, true);
        }
    }

    public static string Git(string dossier, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = dossier, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "-c", "user.name=Essai fictif", "-c", "user.email=essai@exemple.invalid", "-c", "commit.gpgsign=false" }.Concat(args)) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var s = p.StandardOutput.ReadToEnd();
        var e = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"git {string.Join(' ', args)} : {e}");
        return s.Trim();
    }

    /// <summary>Lance l'outil. Hors ligne : tout accès réseau passe par un proxy inexistant.</summary>
    public static Sortie Lancer(string dossier, bool horsLigne, params string[] args)
    {
        var psi = new ProcessStartInfo("dotnet") { WorkingDirectory = dossier, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add(Cli());
        foreach (var a in args) psi.ArgumentList.Add(a);
        if (horsLigne)
            foreach (var v in new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "http_proxy", "https_proxy", "all_proxy" })
                psi.Environment[v] = "http://127.0.0.1:9";
        using var p = Process.Start(psi)!;
        var sortie = p.StandardOutput.ReadToEndAsync();
        var erreurs = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        return new(p.ExitCode, sortie.Result + erreurs.Result);
    }

    public static void Verifier(Sortie s)
    {
        if (s.Code != 0) throw new InvalidOperationException($"code {s.Code} :\n{s.Texte}");
    }

    public static Sortie Analyser(Depot d, bool horsLigne = false, string? regles = null, bool sansHistorique = false, string? rapport = null)
    {
        var a = new List<string> { "analyser", "ClientsDemo.sln", "--regles", regles ?? Regles };
        if (!sansHistorique) { a.Add("--revision-precedente"); a.Add(d.ShaPrecedent); }
        if (rapport is not null) { a.Add("--sortie"); a.Add(rapport); }
        var s = Lancer(d.Racine, horsLigne, a.ToArray());
        Verifier(s);
        return s;
    }

    /// <summary>Copie des règles avec l'état donné pour REN-001.</summary>
    public static string ReglesAvecEtat(string etatRen001)
    {
        var d = Path.Combine(Travail, "regles-" + etatRen001);
        if (Directory.Exists(d)) Directory.Delete(d, true);
        Copier(Regles, d);
        var verrou = JsonNode.Parse(File.ReadAllText(Path.Combine(d, "regles.lock.json")))!;
        foreach (var r in verrou["regles"]!.AsArray()) if ((string)r!["id"]! == "REN-001") r["etat"] = etatRen001;
        File.WriteAllText(Path.Combine(d, "regles.lock.json"), verrou.ToJsonString());
        return d;
    }

    public static string Section(string texte, string titre)
    {
        var debut = texte.IndexOf($"== {titre} ==", StringComparison.Ordinal);
        Assert.True(debut >= 0, $"section {titre} absente :\n{texte}");
        var suite = texte.IndexOf("\n== ", debut + 1, StringComparison.Ordinal);
        return texte[(debut + titre.Length + 7)..(suite < 0 ? texte.Length : suite)].Trim('\n', '\r');
    }

    public static string[] Lignes(string section) => section.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToArray();

    /// <summary>Rapport JSON sans le champ horodatage, pour comparer deux exécutions.</summary>
    public static string SansHorodatage(string fichier)
    {
        var n = JsonNode.Parse(File.ReadAllText(fichier))!.AsObject();
        n.Remove("horodatage");
        return n.ToJsonString();
    }
}
