# Diagnostics locaux et récupération après crash

Nodalis conserve ses diagnostics **uniquement en local**. Aucun journal, rapport technique ou fichier récupéré n'est envoyé automatiquement.

## Emplacement

Par défaut :

`%LOCALAPPDATA%\Nodalis\Diagnostics`

Ce dossier contient :

- `nodalis.log` : journal actif ;
- `nodalis.1.log`, etc. : journaux tournants ;
- `session.active` : marqueur présent tant que Nodalis n'a pas terminé proprement ;
- `Recovery\...` : quarantaine des fichiers temporaires laissés par une écriture atomique interrompue.

## Rotation et rétention

Le journal actif est limité à environ **512 Kio**. Nodalis conserve au maximum **5 fichiers de journal** au total. Les plus anciens sont supprimés automatiquement lors de la rotation.

Le mécanisme de diagnostic ne doit jamais devenir une seconde source de crash : une impossibilité d'écrire le journal est ignorée par l'application principale.

## Détection d'un arrêt anormal

Au démarrage, Nodalis vérifie `session.active`.

- marqueur absent : la session précédente s'est terminée normalement ;
- marqueur présent : le processus précédent n'a pas atteint l'arrêt propre (crash, kill, coupure, etc.).

Le marqueur est recréé pour la session courante puis supprimé lors de la fermeture normale.

## Récupération sûre des fichiers temporaires

Les écritures de documents utilisent des fichiers temporaires nommés comme :

`.nom-du-document.<guid>.tmp`

Après un arrêt anormal, un tel fichier peut rester dans le workspace.

Nodalis ne tente **jamais** de remplacer automatiquement le document canonique avec ce fichier. Il déplace uniquement les temporaires reconnus vers le dossier `Diagnostics\Recovery` pour inspection manuelle.

Cette règle garantit qu'un crash ne peut pas écraser un document valide pendant la récupération.

## Fenêtre Diagnostics

Le bouton **Diagnostics** de la fenêtre principale permet de :

- voir si un arrêt anormal précédent a été détecté ;
- connaître le dossier de diagnostics ;
- ouvrir ce dossier avec l'Explorateur Windows ;
- consulter un rapport technique ;
- copier explicitement ce rapport dans le presse-papiers.

Le rapport contient des informations d'environnement, l'état de la session, les chemins des éventuels fichiers mis en quarantaine et les dernières lignes du journal. Il **ne lit pas le contenu des documents Markdown**.

## Exceptions globales

Nodalis journalise localement :

- les exceptions WPF non gérées ;
- les exceptions non gérées du domaine .NET ;
- les exceptions de tâches non observées ;
- les erreurs fatales rencontrées pendant le démarrage.

Les exceptions de tâches non observées sont marquées comme observées après journalisation. Les autres erreurs fatales ne sont pas masquées : le journal sert au diagnostic, pas à poursuivre dans un état potentiellement incohérent.
