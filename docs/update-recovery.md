# Mise à jour, rollback et récupération Nodalis

## Garanties avant bascule

Une archive locale est intégralement validée avant l'arrêt de Nodalis :
identité produit, cible win-x64, version, compatibilité du schéma de workspace,
taille et SHA-256 de chaque fichier.

Après extraction, release-manifest.json est copié dans le staging. Le processus
updater recalcule les tailles et SHA-256 juste avant la bascule, puis une seconde
fois après déplacement du staging à l'emplacement courant.

## Journal transactionnel

Chaque opération écrit atomiquement un fichier frère nommé
<installation>.update-transaction.json. Il contient l'identifiant, le type
d'opération, les chemins utiles, l'étape durable courante, les dates et un
diagnostic lisible.

Chaque étape est enregistrée avant de poursuivre. Une interruption laisse donc
un état récupérable.

## Validation de démarrage

Après installation de la candidate, l'updater lance :

    Nodalis.exe --smoke-test

Le processus doit terminer avec le code 0 avant 30 secondes. Sinon la candidate
est considérée non démarrable et la version précédente est restaurée
automatiquement.

## Récupération

Une transaction non terminée est traitée de façon conservatrice. Si une version
précédente exécutable existe, elle redevient la version courante.

Lors d'un rollback interrompu, le swap de la version connue courante est
prioritaire. Une candidate non validée est déplacée dans un dossier .failed-*
au lieu d'être détruite immédiatement.

Une mise à jour réussie conserve toujours <installation>.previous avec une
version exécutable pour rollback manuel.

La commande updater recover permet aussi de relire explicitement le journal
d'une transaction interrompue.

## Nettoyage

Les dossiers .failed-* et .rollback-* ne sont nettoyés qu'après une opération
réussie et uniquement lorsqu'ils ont plus de sept jours.

Le dossier .previous n'est pas supprimé par ce nettoyage.

## Compatibilité avec les migrations de workspace

minimumWorkspaceSchemaVersion et maximumWorkspaceSchemaVersion décrivent les
schémas directement compatibles avec la release.

migratableWorkspaceSchemaVersions permet à une release de déclarer les schémas
sources qu'elle sait migrer au premier démarrage.

Une archive incompatible avec le workspace ouvert est rejetée avant la bascule.

## Tests d'interruption

La suite de régression injecte une interruption après chaque étape persistée de
l'application : staging préparé, ancienne version précédente nettoyée, version
courante déplacée, candidate installée, fichiers revérifiés et démarrage
candidate validé.

Chaque cas doit restaurer la dernière version connue bonne. Un test séparé force
l'échec du smoke-test de la candidate et vérifie le rollback automatique.
