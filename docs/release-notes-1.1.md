# Nodalis 1.1.0 — Notes de version (candidat)

**Statut : préparation de la version 1.1 — pas encore une release
stable approuvée.** Les fonctions ci-dessous sont développées sur
`develop` et soumises aux tests automatiques. La validation
interactive Windows 10/11 et le contrôle des issues P0/P1 sont
des conditions préalables au tag stable.

## Nouveau : Inbox

- Capture volontaire de **texte, liens, images PNG du presse-papiers et
  fichiers**. Aucun accès automatique au presse-papiers ni collecte
  d'arrière-plan. Raccourci Windows global configurable
  (**Ctrl+Shift+I** par défaut), menus et palette de commandes.
- Vue de tri avec recherche, filtres, aperçu, sélection multiple,
  statut **En attente / Traité / Archivé**, corbeille distincte,
  restauration et messages d'avertissement.
- Conversion dans les formats natifs : **Notes rapides**, nouveau
  document Markdown, ajout à un document existant
  (`Fiche de Maintenance.md` incluse) ou **tâche Markdown**
  reconnue par les vues ordinaires.
- Reprise d'une conversion interrompue avec destination mémorisée,
  marqueur de provenance et contrôle de révision : pas de second
  document/tâche ajouté accidentellement après un crash.
- Détection **non bloquante** des doublons probables sur sept jours
  avec comparaison SHA-256 du type, du texte et des pièces jointes.
  L'utilisateur garde le droit d'enregistrer le doublon.
- Pièces jointes contrôlées (10 fichiers de 25 Mio, extensions sûres),
  gestion des erreurs I/O **sans fermeture du dialogue**, quarantaine
  récupérable et protection des captures simultanées.

## Interface : worktree amélioré

- Arbre compact : conteneurs techniques masqués sans modification du
  stockage, cartes projets et sélection contrastée.
- Repères **projet + dossier** épinglés pendant le défilement, y
  compris si l'on parcourt une autre application.
- Filtre instantané, densité compacte/confortable, application
  courante, favoris et récents en blocs repliables.

## Compatibilité

- **Pas de migration obligatoire du workspace 1.0**. Son manifeste
  reste au schéma 1, et l'Inbox est créée seulement à la première
  capture explicitement enregistrée.
- Notes rapides, tâches, projets ultra-simples, recherche, liens,
  sauvegardes, restauration et rollback existants sont conservés.
- L'Inbox est entièrement locale, sans service IA ou NuGet tiers.
- Configuration du raccourci et des vues stockée dans les
  préférences de l'utilisateur.
- Les fichiers Markdown et JSON restent accessibles avec un éditeur.

## Limites et vérifications en attente

- La conversion vers une **intervention de maintenance structurée**
  n'est pas dans cette version ; un ajout Markdown simple reste
  possible dans la fiche existante.
- Les pièces jointes exécutables et raccourcis sont volontairement
  refusés. Un fichier de plus de 25 Mio doit être réduit.
- Windows 10/11, DPI 100/125/150 %, raccourcis concurrents, clavier,
  dark mode, erreurs disque réelles : **recette interactive requise**.
- Les issues Inbox #99–#105 et l'EPIC #96 doivent être clôturés avant
  la publication d'une stable 1.1.

## Documents

- [Guide Inbox](inbox-1.1-guide.md)
- [Contrat de stockage Inbox](inbox-persistence-v1.md)
- [Sécurité et récupération](inbox-safety.md)
- [Checklist de qualification 1.1](release-checklist-1.1.md)
- [Dépannage](troubleshooting.md)
