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
    Management/Jalons.md
    Exploitation/Déploiement
    Tests

Les candidats de déplacement proviennent des rôles de templates connus du manifeste.
Les dossiers personnalisés ne sont pas déplacés. Les collisions, sources absentes,
jonctions et liens symboliques bloquent une proposition. Le statut Candidate
ne signifie pas que le déplacement peut déjà être exécuté.

Les **nouveaux** projets Complexes placent leur fichier unique de jalons sous
Management/Jalons.md. Pour les projets Complexes historiques, l'inventaire inclut
la proposition de déplacer le fichier Jalons.md de la racine vers Management,
sans jamais réaliser l'opération. Notes rapides et Glossaire restent inchangés.
Les projets Ultra-simples ou Simples ne sont pas concernés par cet inventaire.

La migration effective exigera un assistant de validation, des sauvegardes,
un journal de reprise, la mise à jour des manifestes, liens relatifs, index,
onglets ouverts et résolveurs métier. Aucun de ces changements destructifs
n’est effectué par ce premier incrément.

## Support des chemins physiques déclarés

Les projets récents peuvent stocker `relativeDirectory` dans chaque section de `.project.json`. Les sections préexistantes, pour lesquelles il reste absent, conservent leur résolution v1.9 par nom. L’éditeur de structure et l’explorateur utilisent ce chemin physique pour charger et déplacer les documents ; les documents de sous-sections ne sont pas comptés deux fois dans les sections parentes. Le renommage d’un libellé de section au chemin explicite ne déplace pas automatiquement son dossier (afin de préserver les liens). Toute suppression de section contenant d’autres sections déclarées est refusée jusqu’à une opération explicite.


Une section dont `relativeDirectory` correspond déjà à la cible v2 est déclarée `AlreadyPlaced` dans le diagnostic, et non `MissingSource` : les nouveaux projets Complexes ne sont donc jamais présentés à tort comme devant être migrés. Aucun état du diagnostic ne permet d’écrire sur le disque.


## Lecteurs métier et import/export (31 octobre 2026)

Le résolveur de singleton maintient les anciennes sources à leur emplacement actuel pour les projets préexistants, et lit `Management/Jalons.md` lorsque le manifeste déclare ce dossier. Il refuse l’ambiguïté si un document à la racine et un document sous Management sont présents simultanément. Les services de jalons, calendrier, couverture, santé et intégrité reposent sur ce chemin actif. Les anciens liens de navigation vers `Jalons.md` ou `Jalons/Jalons.md` sont redirigés vers le document déclaré lorsqu’il existe, sans écriture.

La prévisualisation d'export attribue chaque document à sa section précise sans le dupliquer sous les sections parentes. L'import DOCX utilise les chemins de section déclarés **dans le plan et dans le commit**, y compris pour une nouvelle création Complexe ; son plan de création indique `Management/Jalons.md`. L'import d'un Markdown **dans un rôle singleton** continue d'utiliser un document d'import distinct et ne modifie pas automatiquement le fichier singleton. Les tests incluent la création réelle d’un projet Complexe lors d’un import DOCX et le contrôle des chemins réellement écrits.

Il reste à développer l’exécution de migration guidée, les vérifications de conflits des liens relatifs et un rollback inter-fichiers avant de déplacer des projets déjà utilisés. Ces opérations ne font pas partie de ce lot.
