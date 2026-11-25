using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Analyseur.Moteur;

/// <summary>Parcours des sources d'une révision (sans compilation).</summary>
public static class Sources
{
    private static readonly string[] Exclus = { ".analyseur", "bin", "obj", ".git" };

    public static IEnumerable<string> FichiersCs(string racine)
    {
        var pile = new Stack<string>();
        pile.Push(racine);
        while (pile.Count > 0)
        {
            var d = pile.Pop();
            foreach (var f in Directory.GetFiles(d, "*.cs").OrderBy(x => x, StringComparer.Ordinal)) yield return f;
            foreach (var s in Directory.GetDirectories(d).OrderByDescending(x => x, StringComparer.Ordinal))
                if (!Exclus.Contains(Path.GetFileName(s))) pile.Push(s);
        }
    }

    /// <summary>Chemin relatif au dossier du projet qui contient le fichier.</summary>
    public static string RelatifAuProjet(string fichier, string racine)
    {
        var d = Path.GetDirectoryName(fichier)!;
        while (d.Length >= racine.Length)
        {
            if (Directory.GetFiles(d, "*.csproj").Length > 0) return Path.GetRelativePath(d, fichier).Replace('\\', '/');
            d = Path.GetDirectoryName(d)!;
        }
        return Path.GetRelativePath(racine, fichier).Replace('\\', '/');
    }

    public static bool Herite(ClassDeclarationSyntax c, string baseNom)
        => c.BaseList?.Types.Any(t => t.Type.ToString() == baseNom || t.Type.ToString().EndsWith("." + baseNom, StringComparison.Ordinal)) == true;

    public static string? Chaine(ExpressionSyntax? e) => e switch
    {
        LiteralExpressionSyntax l when l.IsKind(SyntaxKind.StringLiteralExpression) => l.Token.ValueText,
        BinaryExpressionSyntax b when b.IsKind(SyntaxKind.AddExpression) && Chaine(b.Left) is { } g && Chaine(b.Right) is { } d => g + d,
        InterpolatedStringExpressionSyntax i when i.Contents.All(c => c is InterpolatedStringTextSyntax)
            => string.Concat(i.Contents.Cast<InterpolatedStringTextSyntax>().Select(c => c.TextToken.ValueText)),
        _ => null,
    };
}

/// <summary>Étage 1 : extraction des opérations de migration EF Core.</summary>
public static class ExtracteurMigrations
{
    public static List<OperationMigration> Extraire(string racine)
    {
        var ops = new List<OperationMigration>();
        foreach (var fichier in Sources.FichiersCs(racine))
        {
            if (fichier.EndsWith(".Designer.cs", StringComparison.Ordinal)) continue;
            var arbre = CSharpSyntaxTree.ParseText(File.ReadAllText(fichier), path: fichier);
            var classes = arbre.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Where(c => Sources.Herite(c, "Migration")).ToList();
            if (classes.Count == 0) continue;
            var relatif = Sources.RelatifAuProjet(fichier, racine);
            var migration = Path.GetFileNameWithoutExtension(fichier);
            foreach (var c in classes)
                foreach (var m in c.Members.OfType<MethodDeclarationSyntax>().Where(m => m.Identifier.ValueText is "Up" or "Down"))
                    foreach (var appel in m.DescendantNodes().OfType<InvocationExpressionSyntax>())
                    {
                        if (appel.Expression is not MemberAccessExpressionSyntax acces) continue;
                        var nom = acces.Name is GenericNameSyntax g ? g.Identifier.ValueText : acces.Name.Identifier.ValueText;
                        if (nom is not ("DropColumn" or "AddColumn" or "RenameColumn" or "Sql")) continue;
                        var ligne = appel.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                        string? Arg(string n) => Sources.Chaine(appel.ArgumentList.Arguments.FirstOrDefault(a => a.NameColon?.Name.Identifier.ValueText == n)?.Expression);
                        bool? Bool(string n) => appel.ArgumentList.Arguments.FirstOrDefault(a => a.NameColon?.Name.Identifier.ValueText == n)?.Expression switch
                        {
                            LiteralExpressionSyntax l when l.IsKind(SyntaxKind.TrueLiteralExpression) => true,
                            LiteralExpressionSyntax l when l.IsKind(SyntaxKind.FalseLiteralExpression) => false,
                            _ => null,
                        };
                        if (nom == "Sql")
                        {
                            var premier = appel.ArgumentList.Arguments.FirstOrDefault(a => a.NameColon is null || a.NameColon.Name.Identifier.ValueText == "sql")?.Expression;
                            var texte = Sources.Chaine(premier);
                            ops.Add(new(migration, relatif, m.Identifier.ValueText, ligne, nom, null, null, null, null, texte, texte is not null));
                        }
                        else
                        {
                            var nullable = Bool("nullable");
                            if (nom == "AddColumn" && nullable is null) nullable = false; // valeur par défaut d'EF Core
                            ops.Add(new(migration, relatif, m.Identifier.ValueText, ligne, nom, Arg("table"), Arg("name"), Arg("type"), nullable, null, false));
                        }
                    }
        }
        return ops.OrderBy(o => o.Fichier, StringComparer.Ordinal).ThenBy(o => o.Ligne).ToList();
    }

    /// <summary>Migrations présentes dans N et absentes de N-1 (toutes si N-1 est indisponible).</summary>
    public static List<OperationMigration> Nouvelles(List<OperationMigration> n, List<OperationMigration>? precedente)
    {
        if (precedente is null) return n;
        var connues = precedente.Select(o => o.Migration).ToHashSet(StringComparer.Ordinal);
        return n.Where(o => !connues.Contains(o.Migration)).ToList();
    }
}

/// <summary>Étage 1 : extraction du ModelSnapshot.</summary>
public static class ExtracteurModele
{
    public static List<ProprieteModele>? Extraire(string racine)
    {
        foreach (var fichier in Sources.FichiersCs(racine))
        {
            var arbre = CSharpSyntaxTree.ParseText(File.ReadAllText(fichier), path: fichier);
            var snapshot = arbre.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault(c => Sources.Herite(c, "ModelSnapshot"));
            if (snapshot is null) continue;
            var resultat = new List<ProprieteModele>();
            foreach (var entite in snapshot.DescendantNodes().OfType<InvocationExpressionSyntax>()
                         .Where(i => i.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Entity" }
                                     && i.ArgumentList.Arguments.Count == 2
                                     && i.ArgumentList.Arguments[1].Expression is LambdaExpressionSyntax))
            {
                var nomEntite = Sources.Chaine(entite.ArgumentList.Arguments[0].Expression);
                if (nomEntite is null) continue;
                var corps = (LambdaExpressionSyntax)entite.ArgumentList.Arguments[1].Expression;
                var appels = corps.DescendantNodes().OfType<InvocationExpressionSyntax>().ToList();
                var table = appels.Where(a => a.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "ToTable" })
                    .Select(a => Sources.Chaine(a.ArgumentList.Arguments.FirstOrDefault()?.Expression)).FirstOrDefault(t => t is not null);
                foreach (var prop in appels.Where(a => a.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax { Identifier.ValueText: "Property" } }))
                {
                    var nomProp = Sources.Chaine(prop.ArgumentList.Arguments.FirstOrDefault()?.Expression);
                    if (nomProp is null) continue;
                    var typeClr = ((GenericNameSyntax)((MemberAccessExpressionSyntax)prop.Expression).Name).TypeArgumentList.Arguments[0].ToString();
                    // Remonte la chaîne b.Property<T>("X").IsRequired().HasColumnType("...")
                    SyntaxNode courant = prop;
                    string? typeColonne = null, colonne = null;
                    var requis = false;
                    while (courant.Parent is MemberAccessExpressionSyntax ma && ma.Parent is InvocationExpressionSyntax suivant)
                    {
                        switch (ma.Name.Identifier.ValueText)
                        {
                            case "HasColumnType": typeColonne = Sources.Chaine(suivant.ArgumentList.Arguments.FirstOrDefault()?.Expression); break;
                            case "HasColumnName": colonne = Sources.Chaine(suivant.ArgumentList.Arguments.FirstOrDefault()?.Expression); break;
                            case "IsRequired": requis = true; break;
                        }
                        courant = suivant;
                    }
                    var valeur = typeClr is "int" or "long" or "short" or "byte" or "bool" or "decimal" or "double" or "float" or "DateTime" or "Guid";
                    var nullable = typeClr.EndsWith('?') || (!valeur && !requis);
                    resultat.Add(new(nomEntite, nomProp, table ?? nomEntite, colonne ?? nomProp, typeColonne, nullable));
                }
            }
            return resultat;
        }
        return null;
    }
}
