# Architecture de Nodalis

## Principes

Nodalis est une application WPF locale. Son architecture évite volontairement toute dépendance externe.

### Nodalis.App

Couche WPF uniquement :
- fenêtres et contrôles ;
- thèmes ;
- interactions clavier ;
- composition des services.

La couche UI ne doit pas contenir les règles de persistance.

### Nodalis.Core

Domaine pur :
- manifests et modèles métier ;
- règles de hiérarchie ;
- interfaces de services ;
- validation.

Cette couche ne référence ni WPF ni le filesystem.

### Nodalis.Infrastructure

Implémentations locales :
- filesystem ;
- JSON via `System.Text.Json` ;
- import DOCX via BCL .NET à terme ;
- indexation locale à terme.

## Dépendances autorisées

```text
Nodalis.App
  ├── Nodalis.Core
  └── Nodalis.Infrastructure
         └── Nodalis.Core
```

Aucun `PackageReference` n'est autorisé.

## Réseau

Le produit ne doit effectuer aucun appel réseau. Le script
`scripts/verify-constraints.ps1` bloque les usages évidents des API réseau dans
le code source.

Le workflow GitHub Actions utilise naturellement le réseau pour récupérer le
SDK et compiler le dépôt ; cette contrainte concerne l'exécutable Nodalis.

## Persistance

Les fichiers utilisateur doivent rester lisibles sans Nodalis :
- Markdown pour le contenu ;
- JSON explicite pour les métadonnées ;
- pièces jointes conservées comme fichiers normaux.

Les métadonnées JSON sont écrites via fichier temporaire puis remplacement afin
de réduire le risque de corruption lors d'une interruption.
