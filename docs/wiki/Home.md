# Nodalis — documentation officielle

**Nodalis** est une application Windows de prise de notes, documentation et suivi de projets, entièrement locale. Elle réunit des applications, modules, projets et sous-projets dans **un seul workspace**. Les documents restent lisibles en **Markdown** et les données structurelles en **JSON**.

> Ce Wiki décrit la version **1.0.0**. La [[Roadmap]] distingue les évolutions prévues des fonctions livrées.

## Pour commencer

1. [[Installation]] — télécharger, installer et lancer l'application.
2. [[Premiers-pas]] — créer un workspace, une application et son premier projet.
3. [[Profils-de-projets]] — choisir entre Ultra-simple, Simple, Moyen et Complexe.
4. [[Organisation-du-workspace]] — comprendre où se trouvent les données.

## Guide utilisateur

| Besoin | Page |
| --- | --- |
| Écrire, formater et relier des documents | [[Editeur-et-liens]] |
| Retrouver des informations et piloter l'application au clavier | [[Recherche-et-commandes]] |
| Suivre des tâches, jalons et dates | [[Taches-Kanban-et-calendrier]] |
| Capitaliser réunions et décisions | [[Reunions-et-decisions]] |
| Importer DOCX/Markdown et exporter un projet | [[Import-et-export]] |
| Protéger et restaurer ses données | [[Sauvegardes-et-restauration]] |
| Mettre à jour sans perdre son workspace | [[Mise-a-jour-et-compatibilite]] |
| Résoudre un problème | [[Depannage-et-FAQ]] |
| Analyser les impacts potentiels (develop) | [[Analyse-d-impact]] |

## Pour contribuer

Voir [[Architecture-et-contribution]] et la [[Roadmap]]. Le code évolue d'abord sur `develop` ; les livraisons stables sont publiées sur `main`.

## Les principes de Nodalis

- **Local et hors ligne** : pas de compte, cloud ou appel réseau applicatif.
- **Lisibilité des données** : fichiers Markdown, JSON, pièces jointes ordinaires.
- **Un seul workspace** : pas de changement de workspace pour passer d'un projet à un autre.
- **Personnalisable** : profils et templates déterminent l'état initial, sans figer l'évolution des projets.
- **Résilient** : écritures atomiques, corbeille, sauvegardes, diagnostics et mises à jour réversibles.
- **Sans dépendances tierces** : application C# / WPF / .NET 10.

**Liens :** [Code source](https://github.com/TristanStark/Nodalis) · [Version 1.0.0](https://github.com/TristanStark/Nodalis/releases/tag/v1.0.0) · [Signaler un problème](https://github.com/TristanStark/Nodalis/issues)
