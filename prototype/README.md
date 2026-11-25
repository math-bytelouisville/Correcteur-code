# Prototype de l'Analyseur Intranet — lot 1

Premier lot convenu dans `revues/` (v0.1.6) : tests T-001, T-002, T-009 et T-006.
Tous les projets analysés sont **fictifs** (`exemples/`). Aucun modèle de langage n'est utilisé.

## Ce que fait le prototype

- **Étage 1, extraction des faits**
  - lecture syntaxique des migrations EF Core (`DropColumn`, `AddColumn`, `Sql`) et du `ModelSnapshot` ;
  - chargement sémantique des révisions N et N-1 avec Roslyn (`MSBuildWorkspace`), vues Razor comprises.
- **Étage 2, règles JSON** (`regles/`)
  - `MIG-001` (constat de suppression) et `REN-001` (hypothèse de renommage) combinent des prédicats d'une liste fermée, écrits en C# dans `Predicats.Base.dll` ;
  - le moteur ne référence aucun prédicat : il les découvre au démarrage dans les fichiers `Predicats.*.dll` ;
  - chaque règle listée dans `regles.lock.json` est vérifiée par son empreinte SHA-256.
- **Trois états par indice** : `TROUVE`, `ABSENT` ou `NON_VERIFIABLE`. Un indice non vérifiable n'est jamais compté comme trouvé.
- **Affichage**
  - la question « Vouliez-vous renommer cette colonne ? » n'apparaît dans la vue principale que si `usages_transferes` est `TROUVE` ;
  - sinon, l'hypothèse reste dans le détail de `MIG-001` ;
  - une règle `CANDIDATE` n'apparaît que dans la vue des candidates.
- **Aucune restauration ni aucun téléchargement pendant l'analyse.** La préparation (`dotnet restore`) est une étape séparée, à lancer avant.

## Commandes (Windows PowerShell ou Linux)

```
dotnet build prototype/Analyseur.sln
$outil = "prototype/src/Analyseur.Cli/bin/Debug/net8.0/analyseur.dll"

# 1. Préparer les deux révisions (réseau requis la première fois)
dotnet $outil preparer ClientsDemo.sln
dotnet $outil preparer ClientsDemo.sln --revision <sha N-1>

# 2. Analyser (fonctionne réseau coupé)
dotnet $outil analyser ClientsDemo.sln --revision-precedente <sha N-1> --sortie rapport.json

# Ajouter une règle JSON (elle entre en CANDIDATE)
dotnet $outil regles ajouter ma-regle.json
```

La préparation crée `.analyseur/` dans le dépôt analysé. Ce dossier contient la copie de travail de N-1 et le cache de paquets ; il doit être ignoré par Git. L'option `--cache <dossier>` choisit le cache de paquets, et `--hors-ligne` interdit toute source distante.

## Tests

```
dotnet test prototype/Analyseur.sln
```

Les tests construisent des dépôts Git fictifs (base + `exemples/cas/<cas>/avant`, puis `apres`), préparent les révisions dans un cache propre aux tests (`prototype/.cache-tests/`, le cache du poste n'est pas touché) et comparent les messages mot pour mot.

Résultats obtenus : `resultats/lot1-tests.txt` et sorties complètes dans `resultats/lot1-sorties.txt`.

## Limites connues

- **Système d'exécution** : exécuté sous Linux (.NET SDK 8.0.134), pas encore sous Windows.
- **Coupure du réseau** : elle est simulée par un proxy inexistant ; l'analyse n'ouvre aucune connexion par conception, mais cela n'est pas mesuré au niveau système.
- **Fichiers `.Designer.cs`** : ils sont omis des migrations fictives. Les cas T-002 B gardent `Client.Nom` dans le code C# alors que le modèle le retire : c'est une simplification qui laisse le projet compilable.
- **Hors du lot 1** : T-003 (LocalDB), T-004, T-008 et T-010.
