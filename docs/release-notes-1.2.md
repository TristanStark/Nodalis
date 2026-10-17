# Nodalis 1.2 — Notes de version (candidat à qualification)

**Statut : fonctionnalités développées sur la branche develop ; PAS une release
stable, PAS un tag publié.** Les scénarios automatisés sont intégrés à la CI
Windows. Le passage en stable dépend d'une recette humaine Windows 10/11,
du contrôle des issues de l'EPIC #97 et de la checklist 1.2.

## Maintenance avancée : nouveautés

- **Modèle local Markdown** : incident ou intervention identifié par un ID
  stable (INC-/INT-), avec date, résumé, lien optionnel d'intervention vers
  un incident et champs complémentaires libres. Les anciennes fiches sont
  toujours lisibles et modifiables sans transformation automatique.
- **Saisie d'intervention** dans une fiche existante : formulaire compact,
  édition, choix facultatif d'un incident, écriture atomique et prévention
  des conflits d'édition.
- **Historique et recherche** : liste chronologique par projet ou application,
  filtres (texte, dates, tags, catégorie, statut), ouverture de la source et
  indication des entrées incomplètes, sans les masquer.
- **Récurrences suggérées** : hypothèses déterministes et locales, score
  explicable, comparaison de deux incidents, lien ou rejet uniquement sur
  action explicite, possibilité de désactiver cet affichage.
- **Inbox vers maintenance** : convertir une capture texte, image ou fichier
  en incident, intervention ou première intervention liée. Prévisualisation,
  provenance, pièces jointes copiées avec contrôles d'intégrité, reprise
  après interruption, source Inbox conservée.
- **Relations documentaires** : ajouter une relation typée native vers
  un document de spécification, décision ou note technique en stockant le
  GUID du document. Afficher les références directes ou celles des
  interventions explicitement rattachées à un incident ; signaler les
  références cassées après suppression et les retrouver après restauration.

## Compatibilité avec Nodalis 1.0 et 1.1

- Un projet **Ultra-simple** continue de demander uniquement sa référence/
  son nom : il crée **Notes rapides.md** et **Fiche de Maintenance.md**, plus
  son manifeste technique. Aucun nouveau dossier Réunions, Jalons ou Tests
  n'est créé automatiquement.
- Le manifeste du workspace 1.0 ne change pas de schéma à cause de 1.2.
  Les données ajoutées à une fiche sont du Markdown facultatif standard.
  Une ancienne fiche libre reste libre jusqu'à une action utilisateur
  explicite d'ajout.
- L'application n'utilise ni LLM ni réseau pour les suggestions, l'Inbox
  ou la gestion des relations. Aucun package tiers obligatoire n'est
  ajouté pour cette fonctionnalité.
- Les notes rapides, la palette de commandes, les liens, les tâches,
  la recherche, les sauvegardes et les fonctions Inbox 1.1 coexistent
  avec la maintenance 1.2.
- Les écritures sensibles passent par un contrôle de révision du
  document. Une intervention concurrente ou une fiche incohérente
  requiert une résolution manuelle plutôt qu'un écrasement.

## Migration et récupération

Aucune migration 1.0 → 1.2 n'est requise. Avant d'expérimenter,
faire une sauvegarde du workspace et conserver une copie de la fiche
Markdown. Les anciennes sections ne sont pas interprétées ni
converties automatiquement en incidents.

Si la fiche contient un bloc structuré malformé, l'historique l'indique
et les opérations automatiques sensibles refusent de réécrire
la fiche. L'utilisateur peut corriger directement le Markdown
ou restaurer la dernière copie valide, sans opération de migration.

Une conversion Inbox interrompue peut être relancée avec la même
destination. La capture originale et ses pièces jointes restent
récupérables ; le marqueur d'origine empêche une duplication du bloc.

## Limites et éléments restant à qualifier

- Une référence cassée n'est pas réparée automatiquement en recherchant
  un fichier au nom similaire. La restauration doit préserver le
  GUID original.
- Les suggestions de récurrence **ne sont pas des preuves** de doublon :
  leur qualité dépend des symptômes, références et tags saisis.
- Pour un projet Ultra-simple, la conversion structurée exige une
  fiche de maintenance déjà présente ; elle ne crée pas silencieusement
  une nouvelle structure métier.
- Les pièces jointes manquantes ou dont le hash diffère empêchent
  la conversion concernée. La capture source reste disponible.
- Les tests CI ne peuvent pas prouver l'ergonomie sur les véritables
  environnements Windows 10/11, ni tous les DPI, la navigation clavier,
  le dark mode et les lecteurs externes ; une recette réelle est requise.

## Qualification et publication

La suite de régression inclut la nouvelle qualification 1.2 :
projet Ultra-simple réel, fiche historique libre, **128 interventions**,
recherche, données endommagées, conservation octet-à-octet après erreur,
image Inbox manquante/restaurée, absence de doublon et conservation
du manifeste du projet. Les tests existants couvrent également les
liens GUID, la persistance, les recurrences positives/négatives,
les sauvegardes et l'index de l'historique.

Le workflow de release bloque les tags stables **1.2.x** lorsque
l'EPIC #97 ou les issues #106–#112 sont encore ouvertes. Il laisse
les versions candidates 1.2.x-rc.N utilisables pour les recettes,
sans préjuger de la validation visuelle.

Suivre [Checklist 1.2](release-checklist-1.2.md).
Consulter [Contrat de maintenance](maintenance-markdown-1.2.md),
[Interventions](maintenance-interventions-1.2-guide.md),
[Historique](maintenance-history-1.2-guide.md),
[Récurrences](maintenance-recurrence-1.2-guide.md),
[Inbox vers maintenance](maintenance-inbox-1.2-guide.md) et
[Relations](maintenance-relations-1.2-guide.md).
