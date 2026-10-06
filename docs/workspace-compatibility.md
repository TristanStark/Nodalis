# Politique de compatibilité des workspaces Nodalis

## Principe

Nodalis sépare explicitement trois capacités :

- **lire** le contenu du workspace ;
- **écrire** avec le contrat de données compris par l'application ;
- **migrer** un ancien contrat vers le contrat courant.

Une version de Nodalis ne doit jamais écrire dans un workspace dont le
`schemaVersion` est supérieur au schéma qu'elle comprend.

## Matrice actuelle

| Schéma du workspace | Lecture | Écriture | Comportement |
| --- | --- | --- | --- |
| 0, manifest legacy sans `schemaVersion` | Oui | Non avant migration | proposer migration 0 → 1 ; lecture seule de secours possible |
| 1 | Oui | Oui | ouverture normale |
| > 1 | Oui, Markdown seulement | **Non** | lecture seule de secours sur snapshot isolé |

La lecture d'un schéma plus récent ne signifie **pas** que son contrat est
compris. Nodalis ne parse alors pas les manifests métier, ne reconstruit pas les
index et ne lance pas les services de projet. Il présente uniquement une
navigation générique des dossiers et des fichiers `.md`.

## Lecture seule de secours

La lecture seule de secours est volontairement indépendante du schéma métier :

1. Nodalis lit uniquement `schemaVersion` dans `.workspace.json`.
2. L'utilisateur est averti que les fonctionnalités structurées sont
   indisponibles.
3. Le workspace est copié dans un répertoire temporaire isolé.
4. La navigation est reconstruite à partir du système de fichiers, sans parser
   les manifests de la version plus récente.
5. L'éditeur principal est verrouillé en lecture seule et les services
   automatiques d'indexation, sauvegarde et dashboard ne sont pas démarrés.
6. À la fermeture, le snapshot temporaire est supprimé.

L'isolation du snapshot constitue la barrière de dernier recours : même si une
commande d'une ancienne version tentait d'écrire, elle ne pourrait modifier que
la copie temporaire et jamais le workspace source.

Les liens symboliques et jonctions sont refusés avant la copie afin que le
snapshot ne puisse pas sortir du périmètre du workspace.

## Messages actionnables

Pour un schéma legacy connu, Nodalis propose soit la migration protégée par
sauvegarde, soit la lecture seule.

Pour un schéma plus récent, Nodalis indique explicitement :

- le numéro du schéma détecté ;
- le numéro du schéma compris par l'application ;
- que l'écriture est interdite ;
- que seule la consultation Markdown de secours est disponible ;
- qu'installer une version de Nodalis compatible rétablit les fonctionnalités
  structurées.

Un manifest absent ou invalide reste une erreur bloquante : Nodalis ne peut pas
garantir qu'il s'agit d'un workspace valide.

## Évolution de la matrice

Toute nouvelle version de schéma doit mettre à jour cette documentation et les
tests de compatibilité. Le fallback Markdown peut rester lisible pour un schéma
futur parce qu'il ne dépend d'aucun manifest métier ; aucune capacité
d'écriture future n'est déduite automatiquement.
