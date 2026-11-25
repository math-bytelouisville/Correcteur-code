using Analyseur.Moteur;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Predicats.Base;

/// <summary>Recherche, dans la migration analysée et avant la suppression, une instruction SQL qui
/// copie la valeur de la colonne vers une autre destination (INSERT ... SELECT ou UPDATE ... SET).
/// Une simple lecture dans une condition n'est pas une copie. Un SQL non constant ou non
/// analysable donne NON_VERIFIABLE. TROUVE ne dit rien de la complétude de la copie.</summary>
public sealed class CopieAvantSuppression : IPredicat
{
    public string Nom => "copie_avant_suppression";
    public string Version => "1.0";
    public string Libelle => "copie avant la suppression";
    public string Cible => Cibles.Suppression;

    public ResultatPredicat Evaluer(object cible, Faits faits)
    {
        var s = ((CibleSuppression)cible).Suppression;
        var appels = faits.Operations.Where(o => o.Migration == s.Migration && o.Methode == "Up" && o.Type == "Sql" && o.Ligne < s.Ligne).ToList();
        ResultatPredicat? nonVerifiable = null;
        var detail = new List<string>();
        foreach (var appel in appels)
        {
            if (!appel.SqlConstant)
            {
                nonVerifiable ??= ResultatPredicat.NonVerifiable($"texte SQL construit dynamiquement, ligne {appel.Ligne}",
                    new Dictionary<string, string> { ["ligne"] = appel.Ligne.ToString() });
                continue;
            }
            var arbre = new TSql160Parser(initialQuotedIdentifiers: true).Parse(new StringReader(appel.Sql!), out var erreurs);
            if (erreurs.Count > 0)
            {
                nonVerifiable ??= ResultatPredicat.NonVerifiable($"texte SQL non analysable, ligne {appel.Ligne}");
                continue;
            }
            var visiteur = new Visiteur(s.Table!, s.Colonne!);
            arbre.Accept(visiteur);
            if (visiteur.Destination is not null)
            {
                if (visiteur.Filtre is not null)
                    detail.Add($"Filtre appliqué à la copie : {visiteur.Filtre}. La couverture de la copie n'est pas vérifiée.");
                return ResultatPredicat.Trouve($"copie ligne {appel.Ligne} vers {visiteur.Destination}", null,
                    new Dictionary<string, string> { ["ligne"] = appel.Ligne.ToString(), ["destination"] = visiteur.Destination, ["filtre"] = visiteur.Filtre ?? "" },
                    detail);
            }
            if (visiteur.LueDansCondition)
                detail.Add($"{s.Table}.{s.Colonne} est lu ligne {appel.Ligne} dans une condition, sans être copié.");
        }
        return nonVerifiable ?? ResultatPredicat.Absent("aucune copie trouvée dans cette migration", detail);
    }

    private sealed class Visiteur : TSqlFragmentVisitor
    {
        private readonly string _table;
        private readonly string _colonne;
        public string? Destination { get; private set; }
        public string? Filtre { get; private set; }
        public bool LueDansCondition { get; private set; }

        public Visiteur(string table, string colonne) { _table = table; _colonne = colonne; }

        private bool EstColonne(ScalarExpression? e)
            => e is ColumnReferenceExpression c && c.MultiPartIdentifier?.Identifiers.LastOrDefault()?.Value.Equals(_colonne, StringComparison.OrdinalIgnoreCase) == true;

        private bool LitLaTable(FromClause? from)
            => from?.TableReferences.OfType<NamedTableReference>().Any(t => t.SchemaObject.BaseIdentifier.Value.Equals(_table, StringComparison.OrdinalIgnoreCase)) == true;

        private static string Nom(SchemaObjectName n) => string.Join(".", n.Identifiers.Select(i => i.Value));

        private static string Texte(TSqlFragment f)
            => string.Concat(Enumerable.Range(f.FirstTokenIndex, f.LastTokenIndex - f.FirstTokenIndex + 1).Select(i => f.ScriptTokenStream[i].Text));

        public override void ExplicitVisit(InsertStatement node)
        {
            var spec = node.InsertSpecification;
            if (spec.InsertSource is SelectInsertSource { Select: QuerySpecification q } && LitLaTable(q.FromClause)
                && spec.Target is NamedTableReference cibleTable)
            {
                var elements = q.SelectElements.ToList();
                for (var i = 0; i < elements.Count; i++)
                    if (elements[i] is SelectScalarExpression sse && EstColonne(sse.Expression))
                    {
                        var col = i < spec.Columns.Count ? spec.Columns[i].MultiPartIdentifier.Identifiers.Last().Value : $"colonne {i + 1}";
                        Destination ??= $"{Nom(cibleTable.SchemaObject)}.{col}";
                        if (q.WhereClause is not null) Filtre ??= Texte(q.WhereClause);
                    }
                if (Destination is null && q.WhereClause is not null && Contient(q.WhereClause)) LueDansCondition = true;
            }
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(UpdateStatement node)
        {
            var spec = node.UpdateSpecification;
            foreach (var set in spec.SetClauses.OfType<AssignmentSetClause>())
                if (EstColonne(set.NewValue) && spec.Target is NamedTableReference t)
                {
                    Destination ??= $"{Nom(t.SchemaObject)}.{set.Column.MultiPartIdentifier.Identifiers.Last().Value}";
                    if (spec.WhereClause is not null) Filtre ??= Texte(spec.WhereClause);
                }
            if (Destination is null && spec.WhereClause is not null && Contient(spec.WhereClause)) LueDansCondition = true;
            base.ExplicitVisit(node);
        }

        private bool Contient(TSqlFragment f)
        {
            var v = new Chercheur(this);
            f.Accept(v);
            return v.Trouve;
        }

        private sealed class Chercheur : TSqlFragmentVisitor
        {
            private readonly Visiteur _v;
            public bool Trouve;
            public Chercheur(Visiteur v) => _v = v;
            public override void ExplicitVisit(ColumnReferenceExpression node) { if (_v.EstColonne(node)) Trouve = true; }
        }
    }
}
