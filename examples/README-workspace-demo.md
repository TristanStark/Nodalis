# Workspace de démonstration Nodalis

Ce dossier contient un **workspace autonome directement ouvrable dans Nodalis**, construit à partir de l'architecture et des fonctionnalités de l'application, jusqu'à la version **1.6.0** (18 octobre 2026).

## Installation

1. Se placer sur la branche **develop** du dépôt GitHub et cloner le dépôt ou télécharger son ZIP (bouton **Code > Download ZIP**).
2. Extraire le ZIP sur le disque local : une archive encore compressée n'est pas un workspace utilisable.
3. Sur la branche `develop` incluant le sélecteur, ouvrir Nodalis puis utiliser **Système → Changer de workspace…** ou **Ctrl+P → Changer de workspace**. Choisir directement `examples/Nodalis-Demo-Workspace`.
4. Confirmer le dossier, **fermer Nodalis normalement**, puis relancer l'application : le sélecteur programme le workspace pour le prochain démarrage, sans réinitialiser ni modifier le précédent.
5. Ouvrir `DEPART.md` pour la visite guidée.

Sur la version binaire **v1.6.0**, le changement de workspace n'est pas accessible dans l'interface : fermer Nodalis, ouvrir `%LOCALAPPDATA%\\Nodalis\\preferences.json`, remplacer uniquement `workspaceRootPath` par le chemin absolu de ce dossier, enregistrer puis relancer. Conserver l'ancienne valeur pour revenir facilement au workspace de travail.

Ne pas choisir `examples` ou la racine du dépôt. Il ne faut pas non plus copier cette démonstration dans le workspace de travail réel. Faire une copie du dossier de démonstration avant d'y expérimenter suppressions, renommages et éditions.

## Contenu

- Un manifeste de workspace et une application **Nodalis**.
- Un projet **Nodalis - Évolutions 1.x** (complexe), ses documents techniques, décisions, réunions, risques, tests, jalons, rapports, tâches et glossaire.
- Un sous-projet **Recherche lexicale 1.7** (simple), avec ses propres jalons et tests prévisionnels.
- Un module **Expérience utilisateur** et un projet **Refonte explorateur 122** (Ultra-simple), contenant exactement `Notes rapides.md` et `Fiche de Maintenance.md` : incidents et interventions.
- Des liens Markdown et relations typées avec des GUID préenregistrés dans `.nodalis-links.json`, pour les backlinks et l'analyse d'impact.
- Trois vues de démonstration épinglées dans `.nodalis-views.json`.
- Un rapport avec des inclusions `![[GUID#Section]]`, prêt à tester en aperçu et à exporter.
- Deux schémas **BMP locaux** dans `Attachments/`, visibles depuis la documentation même sans réseau.

Le fichier `.nodalis-links.json` **n'est pas à supprimer** : sa collection `targets` porte les identités fixes des documents, utilisées par les relations et inclusions. Nodalis reconstruit les projections `references` et `relations` à l'ouverture ou lors d'un rafraîchissement, sans réinitialiser ces GUID.

## Statut des données

Les fonctionnalités et versions 1.2, 1.4, 1.5 et 1.6 sont décrites d'après le dépôt Nodalis et ses notes de version. Les issues GitHub sont citées lorsque c'est utile. Les personnes/rôles, interventions, réunions, dates futures et tâches en attente forment **un scénario de démonstration** ; ils ne constituent pas un registre historique ni des engagements de livraison. La 1.7 figure comme **travail préparatoire**, pas comme version publiée.

Aucune IA locale, clé, serveur ou authentification n'est nécessaire au chargement.

## Vérifier le jeu de données

Sur Windows avec PowerShell (depuis la racine du dépôt) :

```powershell
./examples/verify-demo-workspace.ps1
```

Le contrôle valide manifests et GUID, références typées, inclusions Markdown, dates et dépendances des jalons, images BMP et vues. Pour contrôler le **rendu réel WPF et la navigation**, il faut ouvrir Nodalis sur Windows.

Documentation détaillée du format : [workspace-format.md](../docs/workspace-format.md). Guides : [v1.4 impact](../docs/impact-user-guide-1.4.md), [v1.5 vues](../docs/views-user-guide-1.5.md) et [v1.6 composites](../docs/composite-documents-1.6.md).
