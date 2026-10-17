# Inbox 1.1 — sécurité, doublons et récupération (#104)

Le stockage Inbox reste **local**, sans surveillance permanente du presse-papiers.
Le contenu n'est lu que par « Importer le presse-papiers » ou via le
sélecteur de fichiers déclenché par l'utilisateur ; les comparaisons de
doublons sont lancées **uniquement après « Enregistrer »**.

## Doublons probables, sans blocage

Les captures récentes (fenêtre standard **7 jours**, configurable en API
entre >0 et 30 jours) sont comparées avec SHA-256. L'empreinte tient
compte du type (Texte/Lien/Image/Fichier), du texte avec retours ligne
normalisés et des noms/contenus des pièces jointes. Les résumés Markdown produits
automatiquement pour une sélection de fichiers sont normalisés afin que
l'ordre de sélection n'influence pas l'empreinte. Pour les images du
presse-papiers, le nom PNG temporaire aléatoire ne participe jamais à
l'empreinte : seuls le type, le texte et les octets PNG comptent. La provenance n'influence pas la détection.

- Les nouvelles captures stockent `fingerprintSha256` dans `item.json`.
- Les anciennes captures dépourvues de champ sont comparées à la demande
  à partir de `capture.md` et des pièces jointes encore accessibles.
  Pas de migration silencieuse ni d'index réseau.
- La recherche couvre la boîte de réception et la **corbeille récupérable**.
- Un dialogue « Doublon probable » affiche le nombre de correspondances,
  la dernière date et le statut ; **Oui** autorise explicitement une
  seconde capture, **Non** laisse la fenêtre ouverte sans écriture.
- Les empreintes seules ne suffisent pas pour supprimer automatiquement
  une capture. L'utilisateur garde le contrôle et ses données.

## Écriture, erreurs et reprise

La fenêtre « Nouvelle capture » n'est plus fermée avant d'avoir terminé
`CaptureAsync`. En cas de refus d'accès, espace insuffisant, disparition
d'un fichier ou erreur de lecture, l'utilisateur voit un diagnostic et
**garde tout le texte dans sa fenêtre**, afin de corriger et réessayer.
L'action Enregistrer est désactivée pendant la vérification/l'écriture,
évite deux clics simultanés et réactive les contrôles après échec.

Comme dans #99–103 :
- Le stockage passe par `Inbox/.staging/` et publie le dossier complet
  par renommage. Une écriture interrompue est récupérée ou déplacée dans
  `Inbox/Quarantaine/`, sans suppression silencieuse.
- La corbeille Inbox conserve l'ID et les pièces jointes. La restauration
  refuse les collisions, sans écraser l'ancienne entrée.
- La conversion conserve son intention de destination et son marqueur
  de provenance ; les tentatives identiques restent idempotentes.
- Les exceptions récupérables sont affichées et journalisées avec
  contexte (« Inbox · validation et détection de doublon »,
  « Inbox · traitement par lot », capture globale, etc.).
- Le raccourci Win32 global ne lit jamais de contenu. Si Windows
  refuse le raccourci, l'erreur est signalée ; la capture depuis
  l'interface reste accessible.

## Pièces jointes

Entre **1 et 10 fichiers**, **25 Mio par fichier** ; texte de capture
limité à **2 Mio UTF-8**. Chemins des copies fondés sur des GUID dans le
workspace. Sélection et copie rejettent les fichiers manquants ou les
liens symboliques et les extensions exécutables, scripts et raccourcis
(les fichiers Markdown, images, PDF, documents, JSON restent acceptés).
Les références persistées ne peuvent pas utiliser de chemin absolu,
traversée de répertoire ou sortie du dossier Inbox géré. Chaque composant
de répertoire des pièces jointes est vérifié pour éviter les jonctions
et les liens symboliques imbriqués.

## Qualification

`InboxSafetySmokeTests` vérifie :
- même contenu/type, autre type, contenu modifié et fenêtre courte ;
- métadonnées héritées sans empreinte ;
- détection en corbeille, restauration et sauvegarde volontaire du doublon ;
- pièces jointes identiques réordonnées, résumés automatiques réordonnés,
  noms PNG temporaires différents pour un même bitmap, et octets modifiés ;
- refus d'extension dangereuse et de fichier >25 Mio ;
- disparition de pièce jointe : absence de publication partielle,
  quarantaine récupérable ; annulation avant publication ;
- métadonnées de pièce jointe falsifiées avec traversal : quarantaine
  sans lecture externe et conservation du Markdown source ;
- erreur I/O contrôlée via collision avec un objet disque.

La CI Windows couvre la compilation WPF, les régressions, le package
portable et sa reproductibilité.

**Recette Windows nécessaire avant fermeture** : simuler refus de
raccourci `Ctrl+Shift+I` déjà utilisé, capture presse-papiers texte/image,
fichier sélectionné supprimé, restauration avec collision, saturation
disque, DPI 100/125/150 %, clavier et actions de confirmation. Vérifier
que le contenu de l'utilisateur reste visible après une erreur et qu'une
capture identique n'est jamais éliminée sans son consentement.
