---
type: maintenance
status: active
tags: ui, worktree, incident, intervention
---
# Fiche de Maintenance

## Contexte

Exemple de **maintenance structurée** basé sur les retours réels remontés lors de l'issue [#122](https://github.com/TristanStark/Nodalis/issues/122). Les enregistrements INC/INT et dates exactes ci-dessous sont **pédagogiques et simulés** ; ils ne prouvent pas un journal d'interventions historique.

## Historique de maintenance

### Incident
- **Identifiant :** INC-11111111111141118111111111111111
- **Date :** 2026-10-17
- **Résumé :** En-tête du projet masqué après défilement vers une autre application
- **Symptômes :** Le projet courant perd son repère visuel lors d'un long scroll
- **Statut :** Résolu
- **Catégorie :** Interface
- **Tags :** worktree, navigation, scroll
> Relation: est lié à | Cible: Parcours utilisateur | ID: bb000000-0000-4000-8000-000000000002

### Intervention
- **Identifiant :** INT-22222222222242228222222222222222
- **Date :** 2026-10-17
- **Résumé :** Ajustement du maintien de l'en-tête actif
- **Incident :** INC-11111111111141118111111111111111
- **Diagnostic :** Le changement de contexte de défilement perturbe le repère
- **Action :** Afficher l'application et le projet sélectionnés de façon persistante
- **Résultat :** Scénario de démonstration de correction
- **Statut :** Terminé
> Relation: documente | Cible: Parcours utilisateur | ID: bb000000-0000-4000-8000-000000000002

### Incident
- **Identifiant :** INC-33333333333343338333333333333333
- **Date :** 2026-10-17
- **Résumé :** Contrôles de l'explorateur partiellement rognés
- **Symptômes :** Boutons d'action hors de la zone visible
- **Statut :** En validation
- **Catégorie :** Interface
- **Tags :** ergonomie, worktree, dpi

### Intervention
- **Identifiant :** INT-44444444444444448444444444444444
- **Date :** 2026-10-18
- **Résumé :** Requalification de la barre d'actions et des marges
- **Incident :** INC-33333333333343338333333333333333
- **Diagnostic :** Largeur disponible insuffisante selon le panneau
- **Action :** Vérifier redimensionnement et DPI 100, 125 et 150 %
- **Résultat :** Contrôles visuels restant à exécuter par l'utilisateur
- **Statut :** À valider

## Procédures

1. Reproduire le scroll dans une application très chargée.
2. Vérifier l'entête actif pendant le défilement.
3. Contrôler l'accès clavier et l'absence de cropping.
4. Conserver les preuves de validation dans [[Plan de recette]].

## Liens métier

[[Parcours utilisateur]] · [[Présentation 1.x]]
