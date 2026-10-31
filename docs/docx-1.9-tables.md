# Nodalis 1.9 — Tableaux Word et fidélité de conversion (#160, premier incrément)

Le parseur WordprocessingML natif détecte désormais `gridSpan` (fusion horizontale), `vMerge` (fusion verticale), les lignes d'en-tête `tblHeader` et les tableaux imbriqués. Les cellules et leur contenu texte restent conservés dans le modèle commun, avec diagnostics associés ; aucune modification du document Word n'est effectuée.

Les tableaux rectangulaires simples restent exportés en Markdown standard. Dès que le document comporte des fusions, des tables imbriquées ou des lignes irrégulières, le convertisseur choisit **une table HTML locale**, en échappant `&`, `<`, `>`, guillemets et apostrophes. Ni scripts, attributs événement, ressources distantes ni relations externes ne sont recopiés.

**Limite affichée explicitement** : les fusions verticales portent l'attribut de diagnostic `data-word-vmerge`, **sans reconstruction parfaite de rowspan**. L'aperçu et le plan indiquent cette perte éventuelle et le DOCX d'origine reste consultable. Les tables complexes sont donc une conversion documentée, non une reproduction pixel-perfect.

Les tests vérifient un DOCX réel minimal ZIP/XML avec entête, colspan, vMerge, tableau imbriqué, texte ressemblant à du script, ainsi que la non-régression des tableaux Markdown simples. La conversion des listes/hyperliens à l'intérieur des cellules et la recette sur tableaux géants restent à compléter avant de fermer #160.
