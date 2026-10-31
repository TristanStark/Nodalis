# Nodalis 1.9 — Affectation visuelle de sections (#158, premier incrément)

L'onglet **Affectation** de l'assistant DOCX montre les sections Word à gauche et l'application, le projet sélectionné ainsi que ses sections existantes à droite. L'utilisateur peut affecter une branche Word par glisser-déposer sur une section ou au clavier avec le bouton **Affecter**. Les deux parcours utilisent la même validation ; une destination n'est jamais acceptée si son nom créerait un chemin Markdown dangereux.

Les actions **Ignorer**, **Monter**, **Descendre** et **Affecter à une nouvelle section** permettent d'exclure le contenu, de choisir l'ordre de fusion quand plusieurs branches ont la même destination, ou de demander explicitement une section manquante. La nouvelle section n'est créée qu'après confirmation globale et par le processus habituel.

La même sélection et le même plan alimentent l'onglet Fichiers, qui reste le garde-fou avant écriture. Les projets Ultra-simples ne reçoivent pas de nouvelle structure par défaut.

**Limites de cette première étape :** toutes les branches visent encore le projet choisi dans l'onglet **Cible**. La sélection de **fichiers existants individuels**, la création d'une destination par bloc et l'affectation simultanée de plusieurs projets seront traitées dans les phases suivantes (#162, #168). L'arbre de droite affiche pour l'instant les **sections de projet**, pas l'inventaire complet des fichiers. Une vérification manuelle Windows DPI/clavier/drag-drop reste nécessaire.
