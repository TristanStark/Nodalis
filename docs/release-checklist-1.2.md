# Checklist de qualification — Nodalis 1.2 (maintenance avancée)

## État et règles de validation

**Document préparatoire : aucune case manuelle n'est considérée validée
par l'exécution de la CI.** L'issue #112 est le point d'entrée de
qualification. Une version RC portable doit être testée sur des
workspaces **fictifs, anonymisés et sauvegardés**.

Chaque campagne enregistre : date, SHA du commit, version RC, version
exacte de Windows, résolution, DPI, langue de l'interface, scénario,
résultat (OK / KO / non testé), capture anonymisée et issue de suivi.

## Vérifications automatisées sur develop

- [ ] CI du **SHA exact candidat** verte : compilation Release, style
  C# sans var et documentation XML, vérification d'accessibilité
  statique, contraintes locales.
- [ ] Nodalis.SmokeTests : modules historiques 1.0, Inbox 1.1 et
  Maintenance 1.2 (y compris MaintenanceReleaseQualificationSmokeTests).
- [ ] Qualification Ultra-simple : **2 Markdown métier + manifeste**,
  aucune création automatique d'autres sections, même après 128
  interventions.
- [ ] Fiche libre de 1.0 : lecture sans transformation ni perte de bytes,
  ajout uniquement sur commande, CRLF / UTF-8 / notes libres conservés.
- [ ] 100+ interventions : 128 en CI, identités et liens incident
  conservés, historique intégral et recherche.
- [ ] Historique endommagé : avertissements et refus des ajouts risqués ;
  aucune auto-migration / réparation cachée.
- [ ] Inbox → intervention : texte/image, pièce jointe absente, restauration
  du fichier, confirmation idempotente et capture source préservée.
- [ ] Récurrences : suggestions positives et négatives, score/reasons
  explicites, pas de fusion automatique.
- [ ] Relations : GUID cibles stables, multiples interventions, suppression,
  restauration, aucune dépendance déduite.
- [ ] Mise en package portable, package reproductible, lancement de
  l'exécutable dans un workspace RC.
- [ ] Relecture des avertissements CI : aucun nouveau défaut P0/P1
  détecté puis masqué par un test.

**Ces cases sont à renseigner en reportant les URL des runs GitHub
Actions et les sorties de tests du commit candidat.**

## Matrice interactive Windows 10 / Windows 11

Tester chaque ligne sur les deux systèmes. Colonnes à remplir par
les personnes qui exécutent réellement les essais.

| Scénario | Windows 10 x64 | Windows 11 x64 |
| --- | --- | --- |
| Ouvrir un workspace 1.0, sans migration automatique | ☐ | ☐ |
| Créer un Ultra-simple avec un seul champ nom/référence | ☐ | ☐ |
| Constater 2 fichiers métier et aucun jalon/réunion imposé | ☐ | ☐ |
| Ouvrir une ancienne fiche libre en lecture/écriture | ☐ | ☐ |
| Ajouter un incident puis 3 interventions depuis les formulaires | ☐ | ☐ |
| Éditer une intervention sans perdre les notes libres | ☐ | ☐ |
| Recherche historique projet puis application, filtre date/tag | ☐ | ☐ |
| Afficher un historique de 128 interventions, défilement et ouverture | ☐ | ☐ |
| Entrée partielle : avertissement, refus d'écriture risquée | ☐ | ☐ |
| Récurrences : score, explications, ignorer, lier, désactiver | ☐ | ☐ |
| Inbox texte → incident puis reprise sans doublon | ☐ | ☐ |
| Inbox image → intervention liée, vignette et pièces jointes | ☐ | ☐ |
| Pièce jointe supprimée : erreur récupérable sans perte de la capture | ☐ | ☐ |
| Ajouter une relation typée vers spécification / décision | ☐ | ☐ |
| Renommer/déplacer la cible : bon fichier ouvert par GUID | ☐ | ☐ |
| Supprimer la cible : référence cassée, puis la restaurer | ☐ | ☐ |
| Plusieurs interventions : références agrégées sans lien inventé | ☐ | ☐ |
| Modification simultanée/externe : conflit lisible, pas d'écrasement | ☐ | ☐ |
| Mode lecture seule : aucune modification de maintenance | ☐ | ☐ |
| Backup avant édition puis restauration de la fiche/du workspace | ☐ | ☐ |
| Régressions : Notes rapides, tâches, jalons, glossaire, liens, Kanban | ☐ | ☐ |

Pour **chaque OS**, vérifier séparément :

- [ ] DPI **100 %, 125 % et 150 %**, y compris fenêtre redimensionnée
  à 1024 × 768 : pas de bouton, contexte ni étiquette rognés.
- [ ] Dark mode : menus, ListBox, ComboBox, DatePicker, infobulles,
  hover, sélection, messages d'erreur et dialogues lisibles.
- [ ] Clavier sans souris : Tab / Shift+Tab, Entrée, Échap, listes,
  lecture écran, noms accessibles et focus cohérent après retour.
- [ ] Esc / Annuler : zéro écriture de fiche ou modification d'état Inbox.
- [ ] Contextes de navigation : projet et dossier visibles au
  défilement, changement d'application, onglet actif non perdu.
- [ ] Test en fenêtre secondaire/split editor avec onglet maintenance
  sale et propre : rien d'écrasé après conversion Inbox.
- [ ] Documentation du comportement avant/après et journaux locaux
  consultables lorsque les écritures sont refusées.

## Scénarios de récupérabilité à exécuter manuellement

1. Sauvegarder le workspace et dupliquer la fiche libre historique.
2. Créer une intervention structurée, puis ajouter une note libre.
3. Modifier le fichier dans un éditeur externe pendant l'ouverture du
   formulaire : vérifier le refus de la seconde écriture.
4. Enlever une pièce jointe d'une capture de test, tenter une
   conversion, constater que l'état et la fiche sont inchangés.
5. Remettre la pièce jointe, réessayer avec les mêmes choix et vérifier
   une seule intervention créée.
6. Ajouter un lien GUID, renommer puis déplacer la cible : retrouver
   le bon document. Supprimer la cible, constater le signalement
   cassé, restaurer la cible originale : retrouver le lien.
7. Restaurer le workspace sauvegardé : réouvrir la fiche ancienne
   sans migration forcée.

## Critères de passage en version stable

- [ ] Les deux matrices Windows sont complètes, sans cas critique KO.
- [ ] P0/P1 identifiés résolus et retestés ; captures anonymisées
  attachées aux issues correspondantes.
- [ ] Les issues #106–#112 et l'EPIC maintenance #97 sont acceptés
  et fermés après validation. Les issues #107–#111 **ne doivent pas
  être closes seulement parce que la CI est verte**.
- [ ] Le commit RC et le commit de publication sont identiques ou
  leurs différences ont fait l'objet d'un nouveau cycle CI/recette.
- [ ] La version candidate portable a été vérifiée sur Windows 10/11 ;
  les sommes SHA256 et les fichiers livrés correspondent au SHA validé.
- [ ] Le gate de publication 1.2.x stable n'est plus bloqué par
  des issues ouvertes, sans le désactiver ou le contourner.
- [ ] Publication explicite du tag v1.2.0 **uniquement après** validation,
  jamais par une automation déclenchée par cette checklist.

## État CI au moment de la rédaction

La CI dispose déjà d'une vérification de contraintes locales,
d'accessibilité statique, de compilation WPF, de smoke tests
sans dépendances, de package portable reproductible et d'un test
de démarrage du binaire Windows. Ce dispositif **ne certifie pas**
les scénarios humains ci-dessus. Les résultats précis sont à
renseigner après le dernier push de l'issue #112.

Références : [Notes de version 1.2](release-notes-1.2.md),
[Contrat Markdown](maintenance-markdown-1.2.md),
[Compatibilité workspace](workspace-compatibility.md) et
[Checklist 1.1](release-checklist-1.1.md).
