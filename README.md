# Nodalis

**Nodalis** est un outil Windows local de prise de notes et d'aide à la gestion
de projet, conçu autour d'un workspace unique, de Markdown et de liens internes.

## État

Développement initial en cours sur la branche `develop`.

Le socle actuellement disponible comprend :
- WPF / C# et dark mode ;
- architecture App / Core / Infrastructure ;
- workspace unique Markdown + JSON ;
- création et navigation Applications / Modules / Projets / Sous-projets ;
- profils de projets pilotés par configuration et templates Markdown ;
- éditeur Markdown avec aperçu riche et autosave sûr ;
- notes rapides aux scopes Global / Application / Projet ;
- recherche exacte regroupée Projet / Application / Global ;
- Command Palette avec ouverture rapide, actions métier et préférences locales ;
- dashboard d'accueil avec favoris, récents, tâches ouvertes et jalons prochains ;
- tâches Markdown consolidées avec filtres de contexte et navigation vers la source ;
- jalons projet éditables avec vue liste et timeline chronologique ;
- assistant de compte-rendu de réunion avec actions Markdown, décisions et contenus collés ;
- Decision Records reliés à leur source, consultables et recherchables par contexte ;
- import DOCX local avec détection Application/Projet, remapping des sections, aperçu Markdown et plan exact des fichiers avant validation ;
- persistance atomique et détection des modifications externes ;
- corbeille Nodalis récupérable pour documents, projets, modules et applications ;
- zéro dépendance NuGet ;
- zéro API réseau dans le produit.

## Raccourcis

| Raccourci | Action |
| --- | --- |
| `Ctrl+N` | Nouvelle note depuis template / Libre |
| `Ctrl+Shift+N` | Nouveau projet / sous-projet |
| `Ctrl+Alt+N` | Capturer une note rapide |
| `Ctrl+Shift+Q` | Voir les notes rapides agrégées |
| `Ctrl+F` | Recherche Projet / Application / Global |
| `Ctrl+K` | Insérer ou ouvrir un lien interne |
| `Ctrl+P` | Command Palette |
| `Ctrl+S` | Forcer l'enregistrement |
| `Ctrl+B` | Gras |
| `Ctrl+I` | Italique |
| `Ctrl+`` | Code inline |

Les scopes métier de connaissance sont volontairement **Global, Application et Projet**.
Les modules servent à structurer une application mais n'ont pas leur propre glossaire
ou fichier de notes rapides.

## Contraintes

- Windows 10/11 x64
- .NET 10
- WPF
- aucun package tiers
- aucun appel externe
- données locales et lisibles hors de Nodalis

## Compiler

```powershell
dotnet restore Nodalis.sln
dotnet build Nodalis.sln -c Release
```

Vérifier les contraintes :

```powershell
./scripts/verify-constraints.ps1
```

Smoke tests sans framework tiers :

```powershell
dotnet run --project tests/Nodalis.SmokeTests/Nodalis.SmokeTests.csproj -c Release
```

## Publication portable

Construire localement l'archive Windows x64 self-contained :

```powershell
./scripts/publish-portable.ps1 -Version "0.1.0"
```

Le script produit :
- `artifacts/Nodalis-win-x64.zip` ;
- `artifacts/SHA256SUMS.txt` ;
- un exécutable `Nodalis.exe` self-contained et single-file ;
- le guide utilisateur et les informations de version dans l'archive.

Le workflow `Portable Windows Release` réalise le même build sur GitHub Actions.
Un tag `vX.Y.Z` publie automatiquement l'archive et son SHA-256 dans une GitHub Release.

Les données utilisateur sont séparées du dossier de l'application : remplacer une
version portable de Nodalis ne modifie pas le workspace. Voir
`docs/README-UTILISATEUR.md` pour les instructions d'installation et de mise à jour.

Voir `docs/architecture.md`, `docs/workspace-format.md` et
`docs/project-profiles.md` pour les décisions de conception actuelles.
