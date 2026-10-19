# Nodalis v1.9 — Comparateur original / Markdown et dry-run (#162, premier incrément)

L'onglet **Comparateur** de l'assistant DOCX affiche côte à côte le **texte Word interprété**, y compris les blocs exclus, et le **Markdown actuellement projeté**, tenant compte des sélections, réaffectations et corrections de titres. Une liste des sections Word permet de choisir le bloc de comparaison ; sa ligne de statut indique si la section est ignorée, sa destination Nodalis ou le nombre de blocs retenus.

Le comparateur expose les valeurs textuelles des tableaux, les alt-text des images et les alertes des objets complexes. Ce n'est pas un rendu Word pixel-perfect, et l'original DOCX reste conservé.

L'onglet **Fichiers** garde la liste des fichiers réellement prévus, y compris les images sélectionnées et réutilisées, et les avertissements. L'utilisateur doit d'abord calculer puis confirmer explicitement le plan. Des incohérences de règles de profil bloquent la confirmation. La preview n'écrit rien.

Limites restantes : diff à la ligne, navigation synchronisée avec l'arbre de sections, détails par fichier cible / pièce jointe, gestion de versions concurrentes entre plan et commit, annulation d'un calcul volumineux, recette WPF DPI/performances. L'issue #162 ne peut pas être fermée à ce stade.
