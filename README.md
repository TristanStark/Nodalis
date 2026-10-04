# Nodalis

**Nodalis** est un outil Windows local de prise de notes et d'aide à la gestion
de projet, conçu autour d'un workspace unique, de Markdown et de liens internes.

## État

Développement initial en cours sur la branche `develop`.

Le premier socle comprend :
- WPF / C# ;
- dark mode ;
- architecture App / Core / Infrastructure ;
- modèles Application / Module / Projet / Sous-projet / Section ;
- workspace Markdown + JSON ;
- persistance JSON atomique ;
- validation des hiérarchies ;
- zéro dépendance NuGet ;
- zéro API réseau dans le produit.

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

Voir `docs/architecture.md` et `docs/workspace-format.md` pour les décisions
de conception actuelles.
