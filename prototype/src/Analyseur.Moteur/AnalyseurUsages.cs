using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;

namespace Analyseur.Moteur;

public sealed record PaireUsage(string AvantFichier, int AvantLigne, string ApresFichier, int ApresLigne);

public sealed record ResultatUsages(Etat Etat, string Raison, IReadOnlyList<PaireUsage> Paires);

/// <summary>Étage 1 sémantique : charge les compilations de N et de N-1 (sans restauration ni
/// téléchargement) et apparie les usages d'une propriété remplacée.</summary>
public sealed class AnalyseurUsages
{
    private readonly string _racineN;
    private readonly string _shaN;
    private readonly RevisionPrecedente _prec;
    private readonly Lazy<Chargement> _n;
    private readonly Lazy<Chargement> _p;

    private sealed record Chargement(IReadOnlyList<Compilation> Compilations, string? Raison);

    private sealed record Site(string MembreId, string MembreAffiche, string Fichier, int Ligne, string Normalise);

    public AnalyseurUsages(string racineN, string shaN, RevisionPrecedente prec)
    {
        _racineN = racineN;
        _shaN = shaN;
        _prec = prec;
        _n = new(() => Charger(racineN, shaN, EtatsPreparation.Lire(racineN, "courante")));
        _p = new(() => prec.Racine is null ? new Chargement(Array.Empty<Compilation>(), prec.RaisonChargement) : Charger(prec.Racine, prec.Sha!, null));
    }

    public ResultatUsages Comparer(string entite, string ancienne, string nouvelle)
    {
        if (!_prec.FichiersDisponibles) return Nv(_prec.RaisonFichiers!);
        if (!_prec.Chargeable) return Nv(_prec.RaisonChargement!);
        var p = _p.Value;
        if (p.Raison is not null) return Nv(p.Raison);
        var n = _n.Value;
        if (n.Raison is not null) return Nv(n.Raison);

        var proprieteAvant = Propriete(p.Compilations, entite, ancienne);
        if (proprieteAvant is null) return Nv($"propriété {Court(entite)}.{ancienne} introuvable dans {_prec.Sha}");
        var proprieteApres = Propriete(n.Compilations, entite, nouvelle);
        if (proprieteApres is null) return Nv($"propriété {Court(entite)}.{nouvelle} introuvable dans {_shaN}");
        var ancienneDansN = Propriete(n.Compilations, entite, ancienne);
        if (ancienneDansN is not null && Sites(n.Compilations, ancienneDansN, null, _racineN).Count > 0)
            return new(Etat.ABSENT, $"{Court(entite)}.{ancienne} encore référencée dans {_shaN}", Array.Empty<PaireUsage>());

        var avant = Sites(p.Compilations, proprieteAvant, null, _prec.Racine!);
        if (avant.Count == 0)
            return new(Etat.ABSENT, $"aucun usage de {Court(entite)}.{ancienne} dans {_prec.Sha}", Array.Empty<PaireUsage>());
        var apres = Sites(n.Compilations, proprieteApres, ancienne, _racineN);

        var paires = new List<PaireUsage>();
        foreach (var groupe in avant.GroupBy(s => s.MembreId, StringComparer.Ordinal))
        {
            var nouveaux = apres.Where(s => s.MembreId == groupe.Key).ToList();
            if (nouveaux.Count == 0) continue; // usage supprimé sans remplaçant : ni preuve ni contre-preuve
            var anciens = groupe.ToList();
            var utilises = new HashSet<Site>();
            foreach (var a in anciens)
            {
                var candidats = nouveaux.Where(s => s.Normalise == a.Normalise).ToList();
                var doublons = anciens.Count(x => x.Normalise == a.Normalise);
                if (candidats.Count != 1 || doublons != 1 || !utilises.Add(candidats[0]))
                    return Nv($"appariement ambigu dans {a.MembreAffiche} : instructions non équivalentes après substitution");
                paires.Add(new(a.Fichier, a.Ligne, candidats[0].Fichier, candidats[0].Ligne));
            }
            if (utilises.Count != nouveaux.Count)
                return Nv($"appariement ambigu dans {anciens[0].MembreAffiche} : instructions non équivalentes après substitution");
        }
        if (paires.Count == 0)
            return new(Etat.ABSENT, $"aucun usage de {Court(entite)}.{ancienne} remplacé par {Court(entite)}.{nouvelle}", paires);
        return new(Etat.TROUVE, "", paires.OrderBy(x => x.AvantFichier, StringComparer.Ordinal).ThenBy(x => x.AvantLigne).ToList());
    }

    private static ResultatUsages Nv(string raison) => new(Etat.NON_VERIFIABLE, raison, Array.Empty<PaireUsage>());

    private static string Court(string entite) => entite[(entite.LastIndexOf('.') + 1)..];

    private static IPropertySymbol? Propriete(IReadOnlyList<Compilation> compilations, string entite, string nom)
        => compilations.Select(c => c.GetTypeByMetadataName(entite)).FirstOrDefault(t => t is not null)?
            .GetMembers(nom).OfType<IPropertySymbol>().FirstOrDefault();

    /// <summary>Usages d'une propriété. Si <paramref name="substitution"/> est donné, l'accès est
    /// remplacé par ce nom avant normalisation, pour comparer à l'instruction de N-1.</summary>
    private static List<Site> Sites(IReadOnlyList<Compilation> compilations, IPropertySymbol propriete, string? substitution, string racine)
    {
        var sites = new List<Site>();
        foreach (var compilation in compilations)
        {
            // La propriété doit être celle de cette compilation (même identité de symbole).
            var cible = compilation.GetTypeByMetadataName(propriete.ContainingType.ToDisplayString())?
                .GetMembers(propriete.Name).OfType<IPropertySymbol>().FirstOrDefault();
            if (cible is null) continue;
            foreach (var arbre in compilation.SyntaxTrees)
            {
                var modele = compilation.GetSemanticModel(arbre);
                foreach (var id in arbre.GetRoot().DescendantNodes().OfType<IdentifierNameSyntax>().Where(i => i.Identifier.ValueText == propriete.Name))
                {
                    var symbole = modele.GetSymbolInfo(id).Symbol;
                    if (!SymbolEqualityComparer.Default.Equals(symbole?.OriginalDefinition, cible)) continue;
                    var instruction = id.Ancestors().FirstOrDefault(a => a is StatementSyntax and not BlockSyntax || a is ArrowExpressionClauseSyntax || a is MemberDeclarationSyntax);
                    if (instruction is null) continue;
                    var texte = substitution is null
                        ? Normaliser(instruction)
                        : Normaliser(instruction.ReplaceNode(id, SyntaxFactory.IdentifierName(substitution)));
                    var membre = Membre(modele.GetEnclosingSymbol(id.SpanStart));
                    var position = id.GetLocation().GetMappedLineSpan();
                    var chemin = position.HasMappedPath ? position.Path : arbre.FilePath;
                    sites.Add(new(membre?.ToDisplayString() ?? "?", membre is null ? "?" : $"{membre.ContainingType?.Name}.{membre.Name}",
                        Path.GetFileName(chemin), position.StartLinePosition.Line + 1, texte));
                }
            }
        }
        return sites;
    }

    private static ISymbol? Membre(ISymbol? s)
    {
        while (s is IMethodSymbol { MethodKind: MethodKind.AnonymousFunction or MethodKind.LocalFunction }) s = s.ContainingSymbol;
        return s;
    }

    /// <summary>Texte de l'instruction sans commentaires, directives #line ni espaces significatifs.</summary>
    private static string Normaliser(SyntaxNode n)
    {
        var sansTrivia = n.ReplaceTrivia(n.DescendantTrivia(descendIntoTrivia: true), (_, _) => default);
        return sansTrivia.NormalizeWhitespace().ToFullString();
    }

    private Chargement Charger(string racine, string sha, EtatPreparation? etat)
    {
        var raison = EtatsPreparation.RaisonNonChargeable(racine, sha, etat);
        if (raison is not null) return new(Array.Empty<Compilation>(), raison);
        var solution = Directory.GetFiles(racine, "*.sln").OrderBy(f => f, StringComparer.Ordinal).FirstOrDefault();
        if (solution is null) return new(Array.Empty<Compilation>(), $"aucune solution .sln dans la révision {sha}");
        try
        {
            using var espace = MSBuildWorkspace.Create();
            var sol = espace.OpenSolutionAsync(solution).GetAwaiter().GetResult();
            var compilations = new List<Compilation>();
            foreach (var projet in sol.Projects.OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                var c = projet.GetCompilationAsync().GetAwaiter().GetResult();
                if (c is null) return new(Array.Empty<Compilation>(), $"compilation impossible du projet {projet.Name} pour {sha}");
                var erreurs = c.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
                if (erreurs.Count > 0)
                    return new(Array.Empty<Compilation>(), $"révision {sha} non chargeable : {erreurs.Count} erreur(s) de compilation ({erreurs[0].Id})");
                compilations.Add(c);
            }
            return new(compilations, null);
        }
        catch (Exception e)
        {
            return new(Array.Empty<Compilation>(), $"révision {sha} non chargeable ({e.GetType().Name})");
        }
    }
}
