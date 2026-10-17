# Dépannage et FAQ Nodalis 1.0

Ce guide traite les incidents utilisateur courants. Les opérations proposées
sont conservatrices : ne supprimez pas manuellement un manifest ou un registre
d'identité pour contourner une erreur.

## Diagnostic rapide

| Symptôme | Vérification | Action recommandée |
| --- | --- | --- |
| Le workspace ne s'ouvre pas | présence et validité de .workspace.json | consulter workspace-compatibility.md et workspace-migrations.md |
| Le workspace s'ouvre en lecture seule | schemaVersion plus récent ou migration requise | utiliser la version compatible ou migrer après sauvegarde |
| Une sauvegarde est refusée | destination située dans ou sous le workspace | choisir un dossier externe |
| Une restauration est refusée | destination non vide ou mauvais workspace | choisir un dossier distinct et vide et vérifier l'archive |
| Une update est refusée | archive, cible, hash ou compatibilité invalides | utiliser une release correcte et vérifier SHA256SUMS.txt |
| Une update échoue au démarrage | candidate non démarrable | laisser l'updater restaurer .previous puis consulter update-recovery.md |
| Un fichier a été modifié hors Nodalis | conflit de session détecté | recharger ou sauvegarder ailleurs avant de fusionner manuellement |
| Un lien est cassé | cible supprimée ou ambiguë | vérifier la cible et reconstruire l'index via les outils de diagnostic |
| Une importation DOCX échoue | source invalide, verrouillée ou sélection incohérente | corriger la source ou le plan ; l'original reste inchangé |
| Nodalis plante | session anormale détectée au redémarrage | ouvrir les diagnostics et consulter crash-diagnostics.md |

## Workspace invalide ou incompatible

.workspace.json est le point d'entrée du workspace.

Ne modifiez pas schemaVersion à la main pour forcer l'ouverture. Une version
plus récente du schéma peut contenir des contrats qu'une ancienne application
ne sait pas écrire sans perte.

Pour un workspace legacy, utilisez le mécanisme de migration prévu.
Pour un workspace plus récent, utilisez la lecture seule de secours ou installez
une version compatible.

## Conflit de modification externe

Nodalis détecte qu'un document ouvert a changé sur disque depuis son chargement.

Le programme refuse d'écraser silencieusement cette modification.

Procédure recommandée :

1. copiez si nécessaire votre version locale dans un nouveau fichier ;
2. rechargez la version disque ;
3. fusionnez manuellement le contenu utile ;
4. enregistrez ensuite la version reconciliée.

## Liens et index

Un lien textuel peut être cassé parce que la cible a été supprimée, qu'elle est
ambiguë ou que son identité ne peut plus être résolue.

Le registre .nodalis-links.json contient la collection targets qui conserve
l'identité technique et les alias des documents. Ne le supprimez pas comme un
cache ordinaire.

Les diagnostics d'intégrité peuvent reconstruire les parties dérivées de
l'index sans réinventer silencieusement les identités.

## Sauvegarde et restauration

Le dossier de sauvegarde doit être hors du workspace.

Une restauration doit viser un dossier distinct et vide. Nodalis valide
l'archive avant extraction et refuse les traversées de chemin, les archives
incomplètes ou un GUID de workspace incompatible.

Voir backups.md.

## Mise à jour et rollback

Une archive de release est rejetée si :

- le produit ou la version ne correspond pas ;
- la cible n'est pas Windows x64 ;
- un fichier attendu manque ;
- une taille ou un SHA-256 ne correspond pas ;
- le schéma du workspace est incompatible ;
- le dossier de l'application et le workspace se chevauchent.

Si la candidate installée ne démarre pas avec le smoke-test, l'updater restaure
la dernière version connue bonne.

Voir update-recovery.md.

## Crash et diagnostics

Après une fermeture anormale, Nodalis conserve des informations techniques
locales permettant d'identifier la dernière session et les fichiers temporaires
à récupérer ou mettre en quarantaine.

Aucune donnée de diagnostic n'est envoyée automatiquement sur le réseau.

Voir crash-diagnostics.md.

## FAQ

### Mes données sont-elles enfermées dans Nodalis ?

Non. Les documents sont principalement en Markdown et les manifests en JSON.
Les pièces jointes restent des fichiers ordinaires.

### Nodalis envoie-t-il des données sur Internet ?

Non. Le produit ne contient pas d'appel réseau applicatif.

### Puis-je modifier un fichier avec un autre éditeur ?

Oui. Nodalis détecte les modifications externes et évite de les écraser
silencieusement lorsque le document est déjà ouvert.

### Puis-je mettre le workspace dans un dossier synchronisé ?

Oui, tant que l'outil de synchronisation externe gère correctement les fichiers.
Nodalis ne dépend pas de ce service. Les conflits externes restent à résoudre
comme tout conflit de fichier.

### Quelle est la source de vérité pour les tâches et jalons ?

Le Markdown. Les vues tâches, Kanban, calendrier et timeline sont des projections
des fichiers du workspace.

### Puis-je supprimer .nodalis-links.json pour repartir de zéro ?

Ce n'est pas recommandé en schéma 1. Les références dérivées peuvent être
reconstruites, mais la collection targets conserve les GUID et alias techniques
des documents.

### Comment revenir à la version précédente de Nodalis ?

Utilisez Rollback. La version précédente est conservée dans .previous après une
mise à jour réussie.

### Puis-je ouvrir un workspace créé par une version plus récente ?

Une ancienne version n'écrit pas dans un schéma qu'elle ne comprend pas. Elle
peut proposer une consultation Markdown de secours en lecture seule.

### Faut-il sauvegarder avant une migration ou une grosse mise à jour ?

Oui. Les migrations disposent de leurs propres protections, mais une sauvegarde
externe valide reste recommandée avant une opération structurelle importante.
