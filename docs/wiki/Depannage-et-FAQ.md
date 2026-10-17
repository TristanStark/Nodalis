# Dépannage et FAQ

## Où sont les diagnostics ?

Dans `%LOCALAPPDATA%\Nodalis\Diagnostics` :

- `nodalis.log` et ses fichiers de rotation ;
- `session.active`, marqueur d'une session inachevée ;
- `Recovery\`, quarantaine des temporaires récupérés.

La fenêtre **Diagnostics** peut afficher et copier un rapport technique. **Aucun rapport n'est envoyé automatiquement**.

## Guide rapide

| Problème | Que vérifier ? |
| --- | --- |
| Le workspace ne s'ouvre pas | `.workspace.json` valide et compatibilité du schéma |
| L'ouverture est en lecture seule | Version de Nodalis trop ancienne ou migration requise |
| Un fichier a changé pendant l'édition | Conflit externe : préserver les deux versions, puis réconcilier |
| Une note ou relation ne se résout plus | Cible renommée, supprimée ou ambiguë ; diagnostics des liens |
| La sauvegarde échoue | Destination hors du workspace, disque disponible |
| La restauration échoue | Dossier cible distinct, **vide**, et archive du bon workspace |
| L'import DOCX échoue | Format `.docx`, fichier accessible et plan d'import valide |
| La mise à jour échoue | Vérifier version, SHA-256, schéma et journaux de récupération |
| Nodalis s'est fermé brutalement | Ouvrir Diagnostics et examiner les fichiers en quarantaine |

## Questions fréquentes

### Les données sont-elles utilisables sans Nodalis ?

**Oui.** Elles sont majoritairement composées de fichiers Markdown, manifests JSON et pièces jointes ordinaires.

### Faut-il un compte ou une connexion Internet ?

**Non.** Le produit ne contient pas d'appel réseau applicatif.

### Puis-je éditer un document avec un autre logiciel ?

**Oui**, mais Nodalis détecte les modifications externes et n'écrase pas silencieusement un fichier modifié depuis son chargement.

### Puis-je synchroniser le workspace avec un service tiers ?

Le workspace est un dossier ordinaire qu'un outil de synchronisation externe peut gérer. Nodalis ne fournit pas de synchronisation cloud et ne peut pas résoudre automatiquement tous les conflits de fichiers externes.

### Puis-je supprimer `.nodalis-links.json` pour reconstruire l'index ?

**Non.** La collection `targets` contient les identifiants et alias de documents. La supprimer peut perdre cette information. Utilisez les outils de diagnostic et les procédures prévues.

### Que se passe-t-il après un crash ?

Nodalis journalise l'arrêt anormal. Les fichiers temporaires reconnus peuvent être déplacés en quarantaine pour inspection ; ils ne remplacent **jamais** automatiquement un document canonique.

### Où demander de l'aide ?

Ouvrir une [issue GitHub](https://github.com/TristanStark/Nodalis/issues) avec la version de Nodalis, Windows 10/11, les étapes pour reproduire et un extrait utile des diagnostics **après vérification qu'il ne contient pas d'informations confidentielles**.

**Références :** [Guide de dépannage](https://github.com/TristanStark/Nodalis/blob/develop/docs/troubleshooting.md) · [Diagnostics](https://github.com/TristanStark/Nodalis/blob/develop/docs/crash-diagnostics.md).
