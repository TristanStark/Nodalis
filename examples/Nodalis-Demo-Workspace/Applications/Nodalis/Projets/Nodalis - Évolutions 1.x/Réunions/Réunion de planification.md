---
type: meeting
status: draft
owner: Équipe Nodalis
tags: reunion, planification
---
# Réunion de planification

**Date :** 2026-10-17

**Participants :** Développement ; Validation ; Produit (rôles de démonstration)

> **IMPORTANT :** Réunion **simulée**, reconstituée à des fins pédagogiques à partir des fonctionnalités présentes dans Nodalis. Aucun échange historique réel n'est attribué à ces personnes.

## Contexte

Préparer la suite de la roadmap Nodalis après la stabilisation des vues dynamiques et des documents composites. En particulier, maintenir la lecture hors ligne et la portabilité.

## Ordre du jour

1. Vérifier l'index des liens internes.
2. Préparer les jeux de tests des vues et documents composites.
3. Cadrer les travaux de recherche locale facultative 1.7.

## Notes

La source de vérité doit rester dans le Markdown. Les liens et relations typed servent les dépendances ; les vues ne doivent pas contenir de copies de données métier.

## Décisions

- Conserver les GUID dans le registre de liens pour les cibles documentaires.
- Ne pas stocker le résultat des vues comme source canonique.
- Distinguer clairement données de démonstration et états réels des issues.

## Actions

- [x] Produire des scénarios de recette pour les composites | Responsable: Validation | Échéance: 2026-10-18 | Priorité: Haute | Statut: Terminé | Tags: recette
- [ ] Examiner le parcours de recherche locale 1.7 | Responsable: Produit | Échéance: 2026-10-24 | Priorité: Normale | Statut: À faire | Tags: recherche
- [ ] Confirmer la navigation du worktree après correction #122 | Responsable: Validation | Échéance: 2026-10-25 | Priorité: Normale | Statut: À faire | Tags: ui

## Transcription IA

Aucune transcription : exemple local sans IA ni audio.

## Résumé IA

Résumé de démonstration **rédigé manuellement** : les documents Markdown restent canoniques, les GUID doivent rester stables et le prototype 1.7 est une prochaine étape de la roadmap.

## Contenu Outlook

Non applicable à ce scénario.

## Références

[[Présentation 1.x]] · [[Décision - Liens stables]] · [[Décision - Markdown canonique]]
