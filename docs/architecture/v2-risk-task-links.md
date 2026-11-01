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

## 7. Navigation bidirectionnelle (incrément suivant)

Les fiches RIS et les tâches conservent les mêmes GUID, mais Nodalis
offre maintenant également une **projection inverse, strictement
consultative**, pour naviguer dans les deux sens :

- **Tâches → Risques :** dans la liste des tâches, une tâche avec
  `PersistentId` et `ProjectId` propose **« Risques liés… »**.
  Le service résout l'ID stable **une seule fois à la demande**,
  revérifie son unicité globale, le projet d'appartenance et le
  chemin de source, puis relit seulement le registre des RIS du
  projet concerné. Aucune recherche n'est déclenchée au scroll
  ou au survol d'une ligne.
- La fenêtre affiche les liens **du plan actuel**, ceux d'**anciens
  plans** et les liens **explicitement déliés**. Une déliaison reste
  un élément historique mais n'est jamais présentée comme active.
  Chaque ligne montre la référence RIS, la mesure, le statut et
  le GUID de version du plan.
- Le bouton **« Ouvrir la fiche RIS »** ramène l'utilisateur dans
  l'éditeur Markdown habituel. Le chemin source est relu et
  contrôlé par la navigation du workspace avant ouverture.
- **Risques → Tâches :** dans **Risques… → Tâches liées…**,
  sélectionner la rubrique, puis une tâche résolue et cliquer
  **« Ouvrir la tâche »**. Le Markdown de la tâche s'ouvre
  dans l'éditeur avec le numéro de ligne comme simple indication
  de navigation. Les GUID orphelins ou ambigus ne sont pas
  proposés comme destinations cliquables.

La projection ne réécrit **aucun fichier** et ne crée aucun lien.
Si le GUID de tâche est supprimé, dupliqué, transféré à un autre
projet ou ne peut plus être vérifié dans le catalogue, la réponse
est vide avec diagnostic plutôt que de rediriger l'utilisateur
vers une fiche du mauvais projet. Le journal RIS, lui, garde
ses relations historiques pour examen.

Le service `ProjectRiskTaskBacklinkService` renvoie les statuts
`Lié · plan actuel`, `Lié · ancien plan` et
`Délié · historique uniquement`. Cette distinction n'infère
ni l'avancement réel d'une tâche, ni la criticité ou la
qualification d'un risque, ni l'état d'une réserve de Gate.

**Tests automatisés :**
`ProjectRiskTaskBacklinkSmokeTests` vérifie les backlinks
sans lien implicite, la résolution du même GUID après déplacement
du fichier, la déliaison historisée, la révision de plan,
le retour des associations au plan courant, le refus de
doublons de GUID entre projets, la disparition d'une source
et l'absence d'écriture dans les fichiers.

**Recette Windows requise :** depuis Tâches, ouvrir « Risques liés… »,
ouvrir le RIS dans l'éditeur, revenir au registre du projet,
ouvrir « Tâches liées… » puis la source de tâche. Tester
également DPI, clavier, fenêtre étroite, déplacement de fichier,
onglets déjà ouverts et tâche sans `PersistentId`. Vérifier
que les fenêtres ne rafraîchissent pas continuellement le Worktree.

**Prochaine étape :** backlinks et relations sûres avec les
réserves des Gates, contrôle de provenance et gestion des
sources orphelines. Pas de libération automatique de réserve.
