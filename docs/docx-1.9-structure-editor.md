# DOCX v1.9 — correction de structure dans l'assistant (#157)

La fenêtre d'import actuelle propose dans l'onglet **Sections** une recherche par titre, et une correction manuelle du bloc Word sélectionné : **Paragraphe/liste** ou **Titre 1 à 9**. La hiérarchie visible est recalculée après confirmation, ainsi que l'aperçu Markdown et le plan d'écriture. Le bouton **Annuler la correction** restaure la classification précédente et ses descendants.

Les blocs exclus restent exclus. Les cases à trois états existantes permettent de sélectionner une branche entière, une seule sous-section ou des tableaux ; la recherche filtre uniquement les sections visibles, pas les données conservées pour l'import. Le DOCX source n'est jamais réécrit.

Les suggestions issues des heuristiques de #156 sont affichées comme informations, jamais automatiquement appliquées. Le changement d'un titre **dans une section déjà détectée** affecte son rendu et ses enfants, mais ne crée pas de nouvelle destination : l'affectation aux fichiers et projets est traitée par #158. Le déplacement entre sections principales et les médias non convertis sont encore hors de ce premier incrément.

Tests : promotion/déclassement, hiérarchie parent/enfant, contrôle de l'aperçu Markdown, invariants du document source et erreur sur identifiant inexistant. Qualification clavier/DPI à confirmer sur Windows réel.
