# Inbox 1.1 — conversion native (#103)

Parent : #96. Accessible depuis **Inbox > Convertir…** pour une capture
sélectionnée hors corbeille, sans changer de workspace.

## Choix et aperçu

Choix de destination : Global, Application ou Projet, y compris projet
ultra-simple. Les destinations proviennent de l'arborescence Nodalis et
non d'un chemin saisi librement. Choix de format :

- Notes rapides : ajout au fichier de notes rapides du périmètre.
- Nouveau document Markdown : fichier à nom choisi, sans overwrite.
- Ajouter à un document existant : liste des Markdown du périmètre,
  y compris Fiche de Maintenance.md, **sans** intervention structurée 1.2.
- Tâche Markdown : ligne de tâche - [ ] dans Tâches.md, reconnue par
  le gestionnaire de tâches et la recherche existants.

La prévisualisation est en lecture seule, avec chemin de destination et
provenance. Après la conversion, l'entrée Inbox est marquée Traité,
associée au document cible et **n'est pas supprimée**. La navigation,
les liens et les tâches sont actualisés après la fermeture de l'Inbox.

## Reprise après échec et traçabilité

Avant toute écriture, Nodalis crée atomiquement
Inbox/Items/<guid>/conversion.json, une intention persistante (source,
mode, destination, cible exacte). Le fragment cible contient un marqueur
Markdown non exécutable unique :

    <!-- nodalis-inbox:<guid> -->

À chaque réessai, Nodalis vérifie l'intention et cherche ce marqueur
dans le fichier de destination. Si le fichier a déjà été écrit,
la conversion n'est **pas répétée** : les métadonnées Inbox peuvent être
finalisées même après une interruption juste après le commit Markdown.
Une tentative vers une autre cible est refusée, plutôt que dupliquée.
À la réouverture du dialogue, le service recharge l'intention et
**pré-sélectionne le même mode et la même cible**, verrouillés jusqu'à la
fin de la reprise. Aucun choix antérieur n'est à mémoriser.

La création d'un nouveau document est atomique **sans overwrite**.
Pour l'ajout à un fichier, TextDocumentSession vérifie les modifications
externes avant d'enregistrer. Les pièces jointes sont copiées dans
Attachments/Inbox-<guid>/ au niveau du fichier cible avec des liens
Markdown relatifs. Leurs copies existantes ne sont acceptées que si
leurs empreintes SHA-256 correspondent. Les chemins avec traversée,
les liens symboliques et les destinations hors du périmètre sont refusés.

En cas d'échec, l'entrée Inbox et les copies déjà effectuées sont
conservées, le diagnostic est présenté et une reprise est possible.

## Qualification

Tests InboxConversionSmokeTests : Global, application, projet,
notes rapides, ajout à Fiche de Maintenance, nouveau document,
tâche dans WorkspaceTaskService, reprise après interruption entre
écriture Markdown et item.json, anti-doublon après relance, collision,
chemins refusés et copie de pièce jointe après reprise.

Recette Windows à faire : choix des cibles, clavier, DPI 100/125/150 %,
panneaux de 240/300/400 px, conflits externes et pièces jointes
manquantes. Aucune capture réelle de workspace ne doit être publiée
sans anonymisation.
