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
- persistance atomique et détection des modifications externes ;
- zéro dépendance NuGet ;
- zéro API réseau dans le produit.

## Raccourcis

| Raccourci | Action |
| --- | --- |
| `Ctrl+N` | Nouvelle note depuis template / Libre |
| `Ctrl+Shift+N` | Nouveau projet / sous-projet |
| `Ctrl+Alt+N` | Capturer une note rapide |
| `Ctrl+F` | Recherche Projet / Application / Global |
| `Ctrl+P` | Command Palette |
| `Ctrl+S` | Forcer l'enregistrement |
| `Ctrl+B` | Gras |
| `Ctrl+I` | Italique |

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

Voir `docs/architecture.md`, `docs/workspace-format.md` et
`docs/project-profiles.md` pour les décisions de conception actuelles.
