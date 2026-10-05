# Profils de projet Nodalis

Les profils **Simple**, **Moyen** et **Complexe** définissent uniquement la
structure initiale d'un projet. Après création, l'utilisateur reste libre
d'ajouter, renommer, déplacer ou supprimer des sections.

Les sous-projets utilisent exactement les mêmes profils : ils sont considérés
comme des projets à part entière.

## Matrice par défaut

| Section | Simple | Moyen | Complexe |
| --- | :---: | :---: | :---: |
| Jalons | ✓ | ✓ | ✓ |
| Technique | ✓ | ✓ | ✓ |
| Glossaire | ✓ | ✓ | ✓ |
| Tests | ✓ | ✓ | ✓ |
| Documentation |  | ✓ | ✓ |
| Réunions | à la demande | ✓ | ✓ |
| Décisions | à la demande | ✓ | ✓ |
| Risques |  | ✓ | ✓ |
| Fonctionnel |  |  | ✓ |
| Exploitation |  |  | ✓ |
| Déploiement |  |  | ✓ |
| Dépendances |  |  | ✓ |

Les dossiers **Réunions** et **Décisions** restent créés automatiquement à la
première utilisation même lorsqu'ils ne font pas partie du profil initial.

## Granularité Technique

### Simple

Le template couvre :
- contexte ;
- réalisation ;
- configuration ;
- points d'attention.

### Moyen

Il ajoute :
- architecture ;
- composants ;
- flux et données ;
- interfaces ;
- exploitation.

### Complexe

Il ajoute encore :
- responsabilités des composants ;
- modèle de données ;
- API ;
- environnements ;
- sécurité ;
- performance et capacité ;
- observabilité ;
- déploiement et rollback ;
- exploitation et reprise ;
- références vers les décisions d'architecture.

## Granularité Tests

### Simple

- périmètre ;
- cas de test ;
- résultats.

### Moyen

- stratégie ;
- environnements et données ;
- unitaires ;
- intégration ;
- fonctionnels / recette ;
- non-régression ;
- résultats et anomalies.

### Complexe

- stratégie et traçabilité ;
- environnements ;
- jeux de données ;
- unitaires ;
- composants ;
- intégration ;
- système ;
- recette fonctionnelle ;
- performance / volumétrie ;
- sécurité ;
- résilience / reprise ;
- non-régression ;
- résultats et anomalies.

## Configuration

La matrice est matérialisée dans :

`Templates/project-profiles.json`

Les contenus générés proviennent des templates Markdown du dossier
`Templates/`. Le choix du profil n'introduit donc aucun comportement codé en
dur dans l'interface WPF.

Nodalis fournit également l'écran **Templates et profils de projet**, accessible
depuis le bouton **Templates** ou depuis la Command Palette. Cet écran permet :

- d'éditer le nom, la catégorie, le nom de fichier généré et le Markdown d'un
  template ;
- de prévisualiser localement le rendu avec des valeurs pour les variables
  `{{...}}` ;
- de dupliquer un template sans écraser l'original ;
- de restaurer individuellement un template intégré ;
- de modifier le nom, l'ordre, le caractère singleton et le template initial
  des sections des profils Simple, Moyen et Complexe ;
- de restaurer individuellement un profil intégré.

L'interface n'introduit **aucun format persistant supplémentaire** :
`Templates/templates.json`, `Templates/project-profiles.json` et les fichiers
Markdown restent les sources canoniques. La validation est effectuée avant
sauvegarde ; une variable ou un nom de fichier invalide est signalé avant
écriture.

Modifier les profils change les structures **des futurs projets uniquement**.
Nodalis ne remodèle jamais silencieusement un projet déjà créé.
