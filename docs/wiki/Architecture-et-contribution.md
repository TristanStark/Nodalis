# Architecture et contribution

## Architecture

Nodalis est une application locale **C# / .NET 10 / WPF**. Le dépôt contient quatre projets principaux :

- `src/Nodalis.App` : interface WPF, thèmes, interactions, composition ;
- `src/Nodalis.Core` : modèles métier, règles, validations et contrats ;
- `src/Nodalis.Infrastructure` : fichiers, JSON, imports, sauvegardes et services locaux ;
- `src/Nodalis.Updater` : déploiement local et récupération de mise à jour.

La direction principale des dépendances est **App → Core et Infrastructure → Core**. La logique métier n'est pas censée dépendre de WPF.

Il n'y a **aucun package NuGet tiers** ni API réseau applicative.

## Compiler

Prérequis de développement : Windows compatible et **SDK .NET 10**.

~~~powershell
git clone https://github.com/TristanStark/Nodalis.git
cd Nodalis
git switch develop

dotnet restore Nodalis.sln
dotnet build Nodalis.sln --configuration Release
~~~

## Tests et contrôles

~~~powershell
dotnet run --project tools/Nodalis.CodeStyle/Nodalis.CodeStyle.csproj --configuration Release -- --check
dotnet run --project tests/Nodalis.SmokeTests/Nodalis.SmokeTests.csproj --configuration Release
.\scripts\verify-constraints.ps1
~~~

La suite SmokeTests est développée **sans framework tiers**. Elle couvre notamment workspace, import/export, tâches, liens, sauvegardes, updater et fixtures Small / Medium / Large.

## Conventions de contribution

Les règles codifiées dans `AGENTS.md` sont obligatoires :

- **Pas de `var`** : types C# explicites.
- **Commentaires XML** pour les méthodes, constructeurs, opérateurs et conversions dans `src/`.
- Documentation XML des paramètres, génériques et retours lorsque concernés.
- Vérification du build, des règles de style et des SmokeTests.
- Préservation des formats Markdown/JSON et de la compatibilité des workspaces.

## Branches

Le travail est réalisé sur **`develop`** ; une livraison qualifiée est ensuite intégrée sur `main`. Le [suivi GitHub](https://github.com/TristanStark/Nodalis/issues) documente les EPICs et corrections.

## Contribution documentaire

La documentation de référence versionnée est dans `docs/`. Les sources de ce Wiki sont dans `docs/wiki/` sur `develop`. Un script de publication du Wiki est fourni dans `scripts/publish-wiki.ps1` ; la procédure est documentée dans `docs/wiki-source.md`.

**Références :** [Architecture](https://github.com/TristanStark/Nodalis/blob/develop/docs/architecture.md) · [Règles C#](https://github.com/TristanStark/Nodalis/blob/develop/AGENTS.md) · [Régression](https://github.com/TristanStark/Nodalis/blob/develop/docs/regression-testing.md).
