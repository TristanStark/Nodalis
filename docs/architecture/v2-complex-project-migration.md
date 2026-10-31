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