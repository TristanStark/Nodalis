# Nodalis 2.0 — relations typées entre mesures RIS et tâches Markdown

**État :** incrément fonctionnel sur `develop`, sans publication 2.0.
Le développement relie volontairement les mesures d'un plan de traitement
aux tâches **identifiées par GUID persistant**, sans automatiser leur exécution.

## 1. Contrat relationnel

Une relation associe exactement quatre identifiants :

- `RiskId` : GUID canonique de la fiche `RIS-xxxx.md`.
- `TreatmentId` : GUID de la **version de plan** humaine et immuable.
- `Kind` : `Prevention`, `Mitigation` ou `Contingency`.
- `TaskId` : `TaskItem.PersistentId`, présent dans le commentaire
  `<!-- nodalis:task:GUID -->` de la checkbox.

**Ne jamais utiliser `TaskItem.Id` lorsque `PersistentId == null`.**
L'ID d'occurrence v1.9 dépend du document, de la ligne et du texte.
Une tâche ancienne se dote de son identité explicitement depuis
la vue Tâches ; la lecture ne modifie jamais un Markdown.

Une relation possède un état **liée ou déliée**, choisi et justifié
par une personne. Ce n'est pas un synonyme de tâche cochée ou de
mesure terminée.

## 2. Interface WPF

Ouvrir le **Parcours du projet → Risques…**, choisir un risque avec
un plan de traitement et cliquer **Tâches liées…**.

L'action « Associer une tâche » présente uniquement les tâches
de ce **même projet** disposant d'un `PersistentId` unique dans le
workspace. L'auteur choisit la rubrique, la checkbox exacte, inscrit
son identité et une justification d'au moins dix caractères. Une
**seconde confirmation** expose le risque, la version de plan, la
mesure, le GUID de tâche et le motif avant toute sauvegarde.

L'action « Dissocier une tâche » affiche seulement les liens actifs
sur la rubrique et la version de plan actuelles. Un GUID dont le
document source est introuvable reste affiché comme
**source absente ou non résolue** et peut être délié par décision
explicite, sans que Nodalis efface son historique.

Si une tâche du projet ne figure pas dans les choix, ouvrir la vue
Tâches et lui attribuer un GUID permanent. En cas de doublon,
corriger le conflit dans les sources avant de créer une relation.
Une tâche de projet extérieur ou une simple occurrence héritée
ne peut jamais être liée.

## 3. Journal Markdown canonique

Le fichier RIS conserve les événements dans un nouveau bloc :

```markdown
<!-- nodalis:risk-task-links:start -->
<!-- nodalis:risk-task-links-head:GUID-DU-DERNIER-EVENEMENT -->
<!-- nodalis:risk-task-link-event:{...} -->
<!-- nodalis:risk-task-links:end -->
```

Chaque événement versionné contient son GUID, le GUID de la fiche,
le GUID de la version de plan, la rubrique, la clé persistante de
la tâche, l'action `Link`/`Unlink`, l'auteur, la justification,
l'heure UTC et le `PreviousId` de l'avis précédent.

La tête protège la dernière entrée contre une disparition isolée ;
la chaîne protège les événements précédents. La lecture refuse les
doublons, transitions sans changement, GUIDs vides, références
à un plan inconnu ou à une version qui n'était pas en vigueur à
la date de l'avis, fragments de journal et avis anonymes.

Le journal de relations est placé **avant le suivi des mesures**,
lui-même avant le plan de traitement, la qualification et les
évaluations. Le reste du Markdown est conservé octet pour octet
dans les écritures effectuées par le service, hors ajout du journal.
Les anciennes fiches sans journal restent **parfaitement lisibles**,
sans migration automatique.

**Important :** l'intégrité est vérifiée par les commandes Nodalis,
sans signature cryptographique externe contre une falsification
malveillante de l'ensemble du Markdown.

## 4. Déplacements, disparitions et versions

- Déplacer le **fichier Markdown d'une tâche** conserve son GUID,
  si la checkbox et son marqueur restent ensemble. Les nouvelles
  résolutions recherchent le GUID dans le catalogue des tâches.
- Si la source disparaît ou est exclue, le journal de relations du
  risque reste lisible et son GUID n'est pas recyclé. L'utilisateur
  peut **délier** la référence orpheline.
- Si un GUID de tâche est dupliqué dans le workspace, aucune
  **nouvelle** liaison n'est autorisée vers cette cible ambiguë.
  Les anciennes relations restent historisées et doivent être
  examinées, jamais réattribuées automatiquement.
- Si le plan de traitement est révisé, les associations de
  l'ancienne version restent **historiques**, sans transfert implicite
  au nouveau `TreatmentId`. La nouvelle version commence sans liens.

## 5. Sécurité et comportement

`ProjectRiskRegistryService.ChangeTaskLinkAsync` vérifie le
projet actif, son manifeste, le GUID de risque, le chemin local
du fichier, son SHA-256 affiché, la dernière version du plan et,
lors de la création d'un lien, la présence unique du marqueur dans
une checkbox réellement lue du même projet. Le candidat est issu
de `WorkspaceTaskService` et de son catalogue incrémental partagé.

Les liens sont enregistrés **uniquement dans le Markdown du risque**,
avec `TextDocumentSession` et refus des versions concurrentes.
Aucun autre document n'est modifié. Cela ne coche aucune tâche,
ne valide aucun GO/No-Go, n'abaisse aucune criticité, ne termine
aucune mesure et ne ferme aucun incident. Projet archivé :
commandes d'écriture refusées.

## 6. Tests et recette manuelle

La suite `ProjectRiskTaskLinkSmokeTests` contrôle :

1. Deux tâches du projet avec GUID stables et une tâche étrangère,
   dont la référence doit être refusée.
2. Lier la prévention et la mitigation à deux GUID distincts,
   puis contrôler les événements et le Markdown de la tâche
   **strictement inchangé**.
3. Refuser un doublon et une déliaison sans relation préalable.
4. Déplacer le document source d'une tâche ; retrouver son GUID.
5. Délier puis relier la tâche, supprimer la source et délier
   explicitement la référence devenue orpheline.
6. Réviser le plan, vérifier que les anciens liens ne sont pas
   automatiquement actifs sur le nouveau plan.
7. Dupliquer manuellement le GUID d'une tâche dans un projet
   extérieur ; refuser toute nouvelle liaison.
8. Modifier la fiche de risque en parallèle et simuler une
   altération de l'en-tête d'événement : refuser les deux cas.

**Recette Windows encore à faire :** fenêtre réduite, clavier,
DPI élevé, sélection des rubriques, tâches avec ID et sans ID,
plusieurs documents ouverts, déplacement entre fichiers et
changements depuis l'éditeur WPF.

**Prochain incrément :** projection navigable des backlinks tâches
vers les RIS et les réserves des Gates, puis contrôle de la
provenance documentaire. Pas de libération automatique de réserve.
