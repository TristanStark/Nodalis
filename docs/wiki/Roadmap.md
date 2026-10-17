# Roadmap — après Nodalis 1.0

**Dernière release stable annoncée : v1.2.0.** La boîte de réception (1.1) et
la maintenance avancée (1.2) sont publiées. L'analyse d'impact (1.3/1.4) est
implémentée progressivement sur `develop`, mais elle n'est pas encore
une release stable ; suivre les issues GitHub pour la qualification.

## 1.1 — Inbox universelle

Objectif : capturer volontairement du texte, des liens, images ou fichiers quand Nodalis tourne, les conserver de façon fiable, puis les convertir en notes, tâches ou documents. Le raccourci global doit être configurable et ne doit pas conduire à une surveillance permanente du presse-papiers.

Suivi : [EPIC #96](https://github.com/TristanStark/Nodalis/issues/96).

## 1.2 — Maintenance avancée

Objectif : enrichir les projets **Ultra-simples** avec une saisie rapide d'incidents/interventions, un historique, des statuts, des références et des suggestions prudentes de récurrence, en conservant un format Markdown lisible.

La création ultra-simple doit rester inchangée : **un seul nom / référence** et seulement `Notes rapides.md` et `Fiche de Maintenance.md` comme documents métier initiaux.

Suivi : [EPIC #97](https://github.com/TristanStark/Nodalis/issues/97).

## 1.3 — Analyse d'impact déterministe

Objectif : calculer les documents **potentiellement affectés** par un changement en s'appuyant sur les relations typées existantes, avec sens de propagation, profondeur, preuves et chemins justificatifs. Une analyse doit rester en lecture seule.

Suivi : [EPIC #98](https://github.com/TristanStark/Nodalis/issues/98).

## 1.4 — Exploration graphique

Objectif : vue graphe WPF, alternative tabulaire accessible, liens avec les maintenances et performance sur les grands workspaces.

Suivi : [EPIC #98](https://github.com/TristanStark/Nodalis/issues/98).

Guide : [[Analyse-d-impact]] (disponible sur `develop`).

## Principes conservés

- Fonctionnement hors ligne et sans dépendances tierces.
- Aucun recours obligatoire à l'IA.
- Markdown et JSON comme données métier portables.
- Protection contre les migrations et mises à jour destructives.
- Ergonomie clavier, interface sombre et stabilité.

**Roadmap détaillée :** [issue #95](https://github.com/TristanStark/Nodalis/issues/95). Une fonctionnalité n'est à considérer comme livrée que lorsqu'elle apparaît dans une release qualifiée.
