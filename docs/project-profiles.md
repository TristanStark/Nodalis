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

Modifier ces fichiers change les structures **des futurs projets uniquement**.
Nodalis ne remodèle jamais silencieusement un projet déjà créé.
