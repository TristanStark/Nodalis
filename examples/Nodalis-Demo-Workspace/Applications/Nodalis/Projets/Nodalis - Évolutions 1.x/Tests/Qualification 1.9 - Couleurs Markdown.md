---
type: test
status: active
owner: Validation
tags: v1.9, qualification, markdown, editeur
---
# Qualification v1.9 — Éditeur Markdown coloré

Vérification du rendu syntaxique source ajouté sur la branche `develop`, sans modification du Markdown enregistré. Tous les essais sont **à faire** ; cocher uniquement après validation manuelle. Noter le commit et les étapes si un défaut apparaît.

## Lisibilité et exactitude du Markdown

- [ ] MD-C01 — Vérifier les titres de niveaux 1 à 6 | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, editeur
  - **Manipulation :** Écrire plusieurs titres `#`, `##`, `###`, puis parcourir un document de plusieurs pages.
  - **Résultat attendu :** Titres visibles en violet, délimiteurs estompés, repères latéraux conservés, sans texte manquant.

- [ ] MD-C02 — Coloration inline sans corruption | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, editeur
  - **Manipulation :** Tester le gras, l'italique, le code inline, les liens Markdown, les liens internes et les citations.
  - **Résultat attendu :** Couleurs distinctes ; contenu strictement inchangé après enregistrement puis réouverture.

- [ ] MD-C03 — Métadonnées et cases de la qualification | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, editeur
  - **Manipulation :** Ouvrir « Qualification 1.9 - Performance WPF », passer une case de `[ ]` à `[x]`, inspecter `Statut`, `Responsable` et `Priorité : Haute`.
  - **Résultat attendu :** Cases ouvertes ambre, terminées vertes, priorité haute rouge ; aucune modification de tâche hors saisie.

## Édition, sélection et performance

- [ ] MD-C04 — Tester curseur, sélection et édition | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, editeur
  - **Manipulation :** Taper dans des titres et des listes, sélectionner des mots colorés, utiliser Ctrl+C/Ctrl+V, Ctrl+Z/Ctrl+Y, sélectionner à la souris.
  - **Résultat attendu :** Saisie et caret alignés, sélection bien visible, aucun caractère dédoublé ou décalé.

- [ ] MD-C05 — Tester retour ligne et longs documents | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, performance
  - **Manipulation :** Ouvrir un fichier de 100 000 à 1 000 000 caractères, écrire puis scroller rapidement et changer la largeur de l'éditeur.
  - **Résultat attendu :** Aucun gel, aucune désynchronisation durable du texte ; retour au rendu natif si le surlignage dépasse son budget.

- [ ] MD-C06 — Tester le double panneau et les onglets | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, editeur
  - **Manipulation :** Scinder l'éditeur, ouvrir deux documents, naviguer rapidement entre 30 onglets puis fermer ceux à droite.
  - **Résultat attendu :** Coloration sur les deux panneaux, aucun contenu fantôme ni régression de fluidité.

- [ ] MD-C07 — Interrupteur et persistance | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, editeur
  - **Manipulation :** Décochez « Couleurs », vérifiez la lisibilité du texte normal, réactivez puis redémarrez Nodalis.
  - **Résultat attendu :** Désactivation immédiate ; réglage mémorisé ; saisie, liens et autosave inchangés.

- [ ] MD-C08 — Alignement en environnement réel | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, editeur
  - **Manipulation :** Tester zoom système 100 %/125 %/150 %, texte accentué et caractères emoji, fenêtre étroite et défilement horizontal.
  - **Résultat attendu :** Texte coloré superposé sans décalage perceptible et repli natif lisible lorsqu'il ne peut pas être aligné.
