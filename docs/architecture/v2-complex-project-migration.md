# Nodalis 2.0 — Inventaire de structure Complexe (#194)

Le service ComplexProjectMigrationPreviewService fournit un aperçu strictement en lecture seule :
il ne modifie aucun dossier, document, manifeste ou lien.

Structure cible envisagée :

    Documentation/Exigences
    Documentation/Fonctionnel
    Documentation/Technique/Architecture
    Analyse/Risques
    Analyse/Dépendances
    Management/Réunions
    Management/Décisions
    Exploitation/Déploiement
    Tests

Les candidats de déplacement proviennent des rôles de templates connus du manifeste.
Les dossiers personnalisés ne sont pas déplacés. Les collisions, sources absentes,
jonctions et liens symboliques bloquent une proposition. Le statut Candidate
ne signifie pas que le déplacement peut déjà être exécuté.

Jalons.md demeure à la racine tant que les lecteurs/raccourcis existants ne sont
pas adaptés à son emplacement futur sous Management. Notes rapides et Glossaire
restent inchangés. Un projet Ultra-simple ou Simple ne peut pas être inspecté
avec cet outil.

La migration effective exigera un assistant de validation, des sauvegardes,
un journal de reprise, la mise à jour des manifestes, liens relatifs, index,
onglets ouverts et résolveurs DOCX. Aucun de ces changements destructifs
n’est effectué par ce premier incrément.

## Support des chemins physiques déclarés

Les projets récents peuvent stocker `relativeDirectory` dans chaque section de `.project.json`. Les sections préexistantes, pour lesquelles il reste absent, conservent leur résolution v1.9 par nom. L’éditeur de structure et l’explorateur utilisent ce chemin physique pour charger et déplacer les documents ; les documents de sous-sections ne sont pas comptés deux fois dans les sections parentes. Le renommage d’un libellé de section au chemin explicite ne déplace pas automatiquement son dossier (afin de préserver les liens). Toute suppression de section contenant d’autres sections déclarées est refusée jusqu’à une opération explicite.


Une section dont `relativeDirectory` correspond déjà à la cible v2 est déclarée `AlreadyPlaced` dans le diagnostic, et non `MissingSource` : les nouveaux projets Complexes ne sont donc jamais présentés à tort comme devant être migrés. Aucun état du diagnostic ne permet d’écrire sur le disque.
